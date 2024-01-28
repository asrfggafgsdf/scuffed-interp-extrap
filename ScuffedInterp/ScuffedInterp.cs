using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
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

        private float _interpAmount = 1;

        private int _stepUpdate = 10;
        private double _oldStep = 10;

        private double _deltaReport = 10;
        private float _deltaReportFloat = 10;

        [Property("Interpolation amount"), DefaultPropertyValue(0.5f), ToolTip
        (
        "Smaller predicts more, bigger predicts less and acts like smoothing above 1." +
        "0 predicts full report ahead. Average 0.5 reports ahead.\n" +
        "0.5 should feel same delay as no filter, but more fucked up. Average about 0 reports behind.\n" +
        "1 should be not fucked up at all, clean direct interpolation. About 1 reports behind.\n" +
        "1.5 may be similiar to bezier interp delay. About 1.5 reports behind."
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
            set {
                if(value > 2f)
                    _resetDelay = value;
                else
                    _resetDelay = 2f; } 
        }
        private float _resetDelay;

        public override PipelinePosition Position => PipelinePosition.PreTransform;

        protected override void ConsumeState()
        {
            if (State is ITabletReport tabletReport)
            {

                if (_isFirst)
                {
                    _stepUpdate = (int)_deltaReport;

                    _pressure = tabletReport.Pressure;
                    _newReport = tabletReport.Position;
                    _oldReport = _newReport;
                    _interpPos = _oldReport;
                 
                    _newVector = _newReport - _oldReport;
                    _oldVector = _newVector;

                    if (_setInterpAmount < 0 || _setInterpAmount >= 1f)
                        _alwaysExtrap = false;

                    _isFirst = false;
                }

                _extrapolation = false;

                _oldStep = (double)_stepUpdate;
                _stepUpdate = 0;

                _pressure = tabletReport.Pressure;
                _oldReport = _newReport;
                _newReport = tabletReport.Position;

                _oldVector = _newVector;
                _newVector = _newReport - _oldReport;

                _deltaReport = 0.998d * _deltaReport + 0.002d * _oldStep;
                _deltaReportFloat = (float)_deltaReport;

                //System.Console.WriteLine("_alwaysExtrap " + _alwaysExtrap + " Prediction error " + ((_newVector[0] - _oldVector[0] - _oldCompositeVector[0]).Length()) / _newVector[0].Length() + " Old error " + ((_newVector[0] - _oldVector[0] - _newVector[1]).Length()) / _newVector[0].Length() );

                _newPredictionVector = _newVector + _newVector - _oldVector;

                _interpAmount = _deltaReportFloat * _setInterpAmount;

                if (_interpAmount < 0f || _alwaysExtrap) //Predict
                {
                    _lerpStepVector = (_newReport - _interpPos + _newPredictionVector * (1f - System.Convert.ToSingle(_alwaysExtrap == false) * _setInterpAmount)) / (_deltaReportFloat + System.Convert.ToSingle(_alwaysExtrap == true) * _interpAmount);
                    
                    _interpPos = _interpPos + (_lerpStepVector / 2);

                    _extrapolation = true;
                }
                else if (_interpAmount >= 0.5f) //Half step is interpolation
                {
                    _lerpStepVector = (_newReport - _interpPos) / _interpAmount;

                    _interpPos = _interpPos + (_lerpStepVector / 2);
                }
                else //Half step starts extrapolating
                {
                    _interpPos = _newReport;
                    _lerpStepVector = (_newReport - _interpPos + _newPredictionVector) / _deltaReportFloat;
                    _interpPos = _interpPos + _lerpStepVector * (0.5f - _interpAmount);

                    _extrapolation = true;
                }
                tabletReport.Position = _interpPos;

                if(_halfStepReports)
                    OnEmit();   
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

                    _interpPos = _interpPos + _lerpStepVector * (_interpAmount - 0.5f * System.Convert.ToSingle(_stepUpdate == 0) - (float)_stepUpdate);

                    _lerpStepVector = (_newReport - _interpPos + _newPredictionVector) / _deltaReportFloat;

                    _interpPos = _interpPos + _lerpStepVector * (1f + 0.5f * System.Convert.ToSingle(_stepUpdate == 0) + (float)_stepUpdate - _interpAmount);
                }
                else //Do a step or half a step if right after a tablet report
                    _interpPos = _interpPos + _lerpStepVector * (0.5f + 0.5f * System.Convert.ToSingle(_stepUpdate != 0));

                tabletReport.Pressure = _pressure;
                tabletReport.Position = _interpPos;

                OnEmit();
            }
            _stepUpdate++;

            if((float)_stepUpdate > _resetDelay * _deltaReportFloat)
            {                    
                _pressure = 0;
                _isFirst = true;
            }
        }
    }
}
