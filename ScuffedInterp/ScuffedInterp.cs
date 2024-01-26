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

        private bool _extrapolation = false;
        private bool _isFirst = true;
        private Vector2 _oldReport;
        private Vector2 _newReport;

        private Vector2 _interpPos;
        private Vector2 _lerpStepVector;

        private Vector2[] _newVector = new Vector2[2];
        private Vector2[] _oldVector = new Vector2[1];

        private Vector2 _newCompositeVector;

        private Vector2 _interpTar;

        //private float _relativePredictionError;

        private float _interpStep = 1;
        private float _extrapStep = 1;
        private float _interpAmount = 1;
        private int _stepUpdate = 1;

        private double _deltaReport = 10;
        private float _deltaReportFloat = 10;
        private double _oldStep = 1;

        private uint _pressure = 0;

        [Property("interp/extrap amount"), DefaultPropertyValue(0.5f), ToolTip
        (
        "0 predict full report ahead or negative (oh no)\n\n" +
        "0.5 should feel same delay as no filter\n" +
        "below 0.5 less delay, more scuffed. above 0.5 more delay, less scuffed\n" +
        "1.5 may be similiar to bezier interp delay. "
        )]
        public float _getSetInterpAmount
        {
            get { return _setInterpAmount; }
            set { _setInterpAmount = value; }
        }
        private float _setInterpAmount;

        [Property("Major cursor path correction"), DefaultPropertyValue(true), ToolTip
        (
        "Always use prediction - even if you could interpolate\n" +
        "is smoother but less loyal to real cursor position/path\n" +
        "does not add delay, probably only a good thing"
        )]
        public bool _getAlwaysExtrap
        {
            get { return _alwaysExtrap; }
            set { _alwaysExtrap = value; }
        }
        private bool _alwaysExtrap;

        [Property("Half-Step reports"), DefaultPropertyValue(true), ToolTip
        (
        "More reports, half step ones. Right when you get a tablet report\n\n" +
        "next cycle does the remaining 'half step'\n" +
        "good for 1000hz"
        )]
        public bool _getHalfStepReports
        {
            get { return _halfStepReports; }
            set { _halfStepReports = value; }
        }
        private bool _halfStepReports;

        [Property("Pen lifting fix"), DefaultPropertyValue(5f), ToolTip
        (
        "Mby don't change´this - 5 default\n" +
        "For relative and other pen lifters\n" +
        "No fucked up shit when lifting pen\n" +
        "a big number disables it mostly, if pen bugs out after lifting try lower."
        )]
        public float _getLiftResetDelay
        {
            get { return _resetDelay; }
            set { _resetDelay = value; }
        }
        private float _resetDelay;

        public override PipelinePosition Position => PipelinePosition.PreTransform;

        protected override void ConsumeState()
        {
            if (State is ITabletReport tabletReport)
            {

                if (_isFirst)
                {
                    _pressure = tabletReport.Pressure;
                    _newReport = tabletReport.Position;
                    _oldReport = _newReport;
                    _interpPos = _oldReport;
                    _newVector[0] = _newReport - _oldReport;

                    _stepUpdate = (int)_deltaReport;

                    _oldVector[0] = _newVector[0];
                    //_relativePredictionError = 1f;

                    _isFirst = false;
                }

                _extrapolation = false;

                _oldStep = (double)_stepUpdate;
                _stepUpdate = 0;

                _pressure = tabletReport.Pressure;
                _oldReport = _newReport;
                _newReport = tabletReport.Position;
                _interpTar = _newReport;
                _oldVector[0] = _newVector[0];

                _newVector[0] = _newReport - _oldReport;

                _deltaReport = 0.995d * _deltaReport + 0.005d * _oldStep;
                _deltaReportFloat = (float)_deltaReport;

                //System.Console.WriteLine("Delay in reports " + _interpAmount / _deltaReport + " Prediction error " + ((_newVector[0] - _oldVector[0] - _oldCompositeVector[0]).Length()) / _newVector[0].Length() + " Old error " + ((_newVector[0] - _oldVector[0] - _newVector[1]).Length()) / _newVector[0].Length() );

                _newVector[1] = _newVector[0] - _oldVector[0];

                _newCompositeVector = _newVector[0] + _newVector[1];

                _interpAmount = _deltaReportFloat * _setInterpAmount;

                if (_interpAmount >= (_resetDelay - 1f) * _deltaReportFloat)
                    _interpAmount = (_resetDelay - 1f) * _deltaReportFloat;
                
                _extrapStep = (1f / _deltaReportFloat);

                if(_alwaysExtrap && _interpAmount > 0)
                { 
                    _extrapStep = 1f / (_deltaReportFloat + _interpAmount);
                    _lerpStepVector = Vector2.Lerp(_interpPos, _newReport + _newCompositeVector, _extrapStep) - _interpPos;
                    _interpPos = _interpPos + (_lerpStepVector / 2);
                    _extrapolation = true;
                }
                else if (_interpAmount <= 0f)
                {
                    _lerpStepVector = Vector2.Lerp(_interpPos, _newReport + _newCompositeVector * (_deltaReportFloat - _interpAmount) / _deltaReportFloat, _extrapStep) - _interpPos;
                    _interpPos = _interpPos + (_lerpStepVector / 2);
                    _extrapolation = true;
                }
                else if (_interpAmount >= 1.0f)
                {

                    _interpStep = 1f / _interpAmount;
                    _lerpStepVector = Vector2.Lerp(_interpPos, _interpTar, _interpStep) - _interpPos;
                    _interpPos = _interpPos + (_lerpStepVector / 2);
                }
                else if (_interpAmount >= 0.5f)
                {

                    _lerpStepVector = _interpTar - _interpPos;
                    _lerpStepVector = _lerpStepVector / _interpAmount;
                    _interpPos = _interpPos + (_lerpStepVector / 2);
                }
                else
                {
                    _interpPos = _interpTar;
                    _lerpStepVector = Vector2.Lerp(_interpPos, _newReport + _newCompositeVector, _extrapStep) - _interpPos;
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
                
                if ((float)(_stepUpdate + 1) >= _interpAmount && _extrapolation == false) //First cycle of extrapolation (third cycle if interp setting 2) - calculate extrapolation vector
                {
                    _extrapolation = true;

                    if (_stepUpdate != 0)
                    {
                        _interpPos = _interpPos + ((_interpAmount - (float)_stepUpdate) * _lerpStepVector);

                        _lerpStepVector = Vector2.Lerp(_interpPos, _newReport + _newCompositeVector, _extrapStep) - _interpPos;

                        _interpPos = _interpPos + ((1f - (_interpAmount - (float)_stepUpdate)) * _lerpStepVector);
                    }
                    else
                    {
                        _interpPos = _interpPos + _lerpStepVector * (_interpAmount - 0.5f);

                        _lerpStepVector = Vector2.Lerp(_interpPos, _newReport + _newCompositeVector, _extrapStep) - _interpPos;

                        _interpPos = _interpPos + _lerpStepVector * (1.5f - _interpAmount);
                    }
                }
                else if (_stepUpdate != 0)
                    _interpPos = _interpPos + _lerpStepVector; 
                else
                    _interpPos = _interpPos + _lerpStepVector / 2f; 

                tabletReport.Pressure = _pressure;
                tabletReport.Position = _interpPos;
                OnEmit();
                
            }
            _stepUpdate++;
            if(_stepUpdate > _resetDelay * (int)_deltaReportFloat)
            {                    
                _pressure = 0;
                _isFirst = true;
            }
        }
    }
}
