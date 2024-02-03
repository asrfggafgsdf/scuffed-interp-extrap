using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;
using System.Numerics;

namespace ScuffedInterpolator
{
    [PluginName("jaaakb's ScuffedInter/Extrapolator")]
    public class ScuffedInterp : AsyncPositionedPipelineElement<IDeviceReport>
    {
        public ScuffedInterp() : base()
        {
        }
        private bool _isFirst = true;
        private bool _extrapolation = false;

        private uint _pressure = 0;

        private Vector2 _newReport;
        private Vector2 _oldReport;

        private Vector2 _interpPos;

        private Vector2 _newVector;
        private Vector2 _oldVector;

        private Vector2 _newPredictionVector;
        private Vector2 _lerpStepVector;

        private float _interpAmount;

        private int _stepUpdate;

        private double _deltaReport = 10;
        private float _deltaReportFloat = 10;

        private HPETDeltaStopwatch _consumeWatch = new HPETDeltaStopwatch(startRunning: false);
        private double _stepDelta;
        private float _stepOffset;


        [Property("Interpolation amount"), DefaultPropertyValue(0.5f), ToolTip
        (
        "Smaller predicts more, bigger predicts less and acts like smoothing above 1."
        )]
        public float _getSetScuffedInterpAmount
        {
            get { return _setInterpAmount; }
            set { _setInterpAmount = value; }
        }
        private float _setInterpAmount;

        [Property("Major cursor path correction"), DefaultPropertyValue(true), ToolTip
        (
        "Always use prediction - even if you could interpolate.\n" +
        "Is prettier, but less loyal to real cursor position/path.\n" +
        "Disable to see if your settings are very fucked up.\n" +
        "Only does something if Interpolation amount is between 0 and 1."
        )]
        public bool _getScuffedAlwaysExtrap
        {
            get { return _alwaysExtrap; }
            set { _alwaysExtrap = value; }
        }
        private bool _alwaysExtrap;

        [Property("Half-Step reports"), DefaultPropertyValue(true), ToolTip
        (
        "More reports, half step ones. Right when you get a tablet report\n\n" +
        "Next cycle does the remaining 'half step'\n" +
        "Good for 1000hz, 'free' extra reports without custom clock."
        )]
        public bool _getScuffedHalfStepReports
        {
            get { return _halfStepReports; }
            set { _halfStepReports = value; }
        }
        private bool _halfStepReports;

        [Property("Pen lifting fix"), DefaultPropertyValue(4f), ToolTip
        (
        "4 default, 2 min\n" +
        "How many tablet reports to 'miss' before resetting pen.\n" +
        "If pen bugs out after lifting for a very short time, try lower value."
        )]
        public float _getScuffedLiftResetDelay
        {
            get { return _resetDelay; }
            set
            {
                if (value > 2f)
                    _resetDelay = value;
                else
                    _resetDelay = 2f;
            }
        }
        private float _resetDelay;

        public override PipelinePosition Position => PipelinePosition.PreTransform;

        protected override void ConsumeState()
        {
            if (State is ITabletReport tabletReport)
            {

                if (_isFirst)
                {
                    _stepUpdate = (int)_deltaReportFloat;

                    _pressure = tabletReport.Pressure;
                    _newReport = tabletReport.Position;
                    _oldReport = _newReport;
                    _interpPos = _oldReport;

                    _newVector = _newReport - _oldReport;
                    _oldVector = _newVector;

                    if (_setInterpAmount < 0 || _setInterpAmount >= 1f)
                        _alwaysExtrap = false;

                    _consumeWatch.Start();

                    _stepDelta = (double)(1f / Frequency);

                    _isFirst = false;
                }

                _extrapolation = false;

                _pressure = tabletReport.Pressure;
                _oldReport = _newReport;
                _newReport = tabletReport.Position;

                _oldVector = _newVector;
                _newVector = _newReport - _oldReport;

                //Debug/test
                //System.Console.WriteLine("_stepDelta " + _stepDelta + " _stepOffset " + _stepOffset + " Prediction error " + ((_newVector - _newPredictionVector).Length()) / _newVector.Length());

                _newPredictionVector = _newVector + _newVector - _oldVector;

                _stepOffset = (float)(_consumeWatch.Elapsed.TotalMilliseconds / _stepDelta);

                _interpAmount = _deltaReportFloat * _setInterpAmount + _stepOffset;

                if (_setInterpAmount < 0f || _alwaysExtrap) //Predict
                {
                    _lerpStepVector = (_newReport - _interpPos + _newPredictionVector * (1f - System.Convert.ToSingle(_alwaysExtrap == false) * (_interpAmount) / _deltaReportFloat)) / (_deltaReportFloat + System.Convert.ToSingle(_alwaysExtrap == true) * _interpAmount);

                    _interpPos = _interpPos + _lerpStepVector * _stepOffset;

                    _extrapolation = true;
                }
                else if (_interpAmount >= _stepOffset) //Half step is interpolation
                {
                    _lerpStepVector = (_newReport - _interpPos) / _interpAmount;

                    _interpPos = _interpPos + _lerpStepVector * _stepOffset;
                }
                else //Half step starts extrapolating
                {
                    _interpPos = _newReport;
                    _lerpStepVector = (_newReport - _interpPos + _newPredictionVector) / _deltaReportFloat;
                    _interpPos = _interpPos + _lerpStepVector * (_stepOffset - _interpAmount);

                    _extrapolation = true;
                }
             

                if (_halfStepReports)
                {
                    tabletReport.Position = _interpPos;
                    OnEmit();
                }
                
                _deltaReport = 0.999d * _deltaReport + 0.001d * (double)_stepUpdate;
                _deltaReportFloat = (float)_deltaReport;
                _stepUpdate = 0;
            }
            else
            {
                OnEmit();   //Apparently buttons do not work without this
            }
        }

        protected override void UpdateState()
        {
            if (State is ITabletReport tabletReport && PenIsInRange())
            {
                if ((float)(_stepUpdate + 1) >= _interpAmount && _extrapolation == false) //Change from interpolation to extrapolation
                {
                    _extrapolation = true;

                    _interpPos = _interpPos + _lerpStepVector * (_interpAmount - _stepOffset * System.Convert.ToSingle(_stepUpdate == 0) - (float)_stepUpdate);

                    _lerpStepVector = (_newReport - _interpPos + _newPredictionVector) / _deltaReportFloat;

                    _interpPos = _interpPos + _lerpStepVector * (1f + _stepOffset * System.Convert.ToSingle(_stepUpdate == 0) + (float)_stepUpdate - _interpAmount); //closer look at if it works with _deltaOffset
                }
                else //Do a step or half a step if right after a tablet report
                    _interpPos = _interpPos + _lerpStepVector * (1f - _stepOffset * System.Convert.ToSingle(_stepUpdate == 0));

                tabletReport.Pressure = _pressure;
                tabletReport.Position = _interpPos;
                OnEmit();

                _stepDelta = 0.999d * _stepDelta + 0.001d * _consumeWatch.Restart().TotalMilliseconds;
            }
            _stepUpdate++;

            if ((float)_stepUpdate > _resetDelay * _deltaReportFloat)
            {
                _consumeWatch.Stop();
                _pressure = 0;
                _isFirst = true;
            }
        }
    }
}
