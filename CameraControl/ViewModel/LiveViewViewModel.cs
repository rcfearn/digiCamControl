﻿using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AForge;
using AForge.Imaging;
using AForge.Imaging.Filters;
using AForge.Vision.Motion;
using CameraControl.Core;
using CameraControl.Core.Classes;
using CameraControl.Core.Translation;
using CameraControl.Devices;
using CameraControl.Devices.Classes;
using CameraControl.Devices.Others;
using CameraControl.windows;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Timer = System.Timers.Timer;

namespace CameraControl.ViewModel
{
    public class LiveViewViewModel : ViewModelBase
    {
        private const int DesiredFrameRate = 20;

        private bool _operInProgress = false;
        private int _totalframes = 0;
        private DateTime _framestart;
        private int _retries = 0;
        private MotionDetector _detector;
        private DateTime _photoCapturedTime;
        private Timer _timer = new Timer(1000/DesiredFrameRate);
        private Timer _freezeTimer = new Timer();
        private Timer _focusStackingTimer = new Timer(1000);
        private Timer _restartTimer = new Timer(1000);
        private DateTime _restartTimerStartTime;
        private int _focusStackingTick = 0;
        private BackgroundWorker _worker = new BackgroundWorker();
        private bool _focusStackingPreview = false;
        private bool _focusIProgress = false;

        private ICameraDevice _cameraDevice;
        private CameraProperty _cameraProperty;
        private BitmapSource _bitmap;
        private int _fps;
        private bool _recording;
        private string _recButtonText;
        private int _gridType;
        private AsyncObservableCollection<string> _overlays;
        private double _currentMotionIndex;
        private bool _triggerOnMotion;
        private PointCollection _luminanceHistogramPoints = null;
        private BitmapSource _preview;
        private bool _isBusy;
        private int _photoNo;
        private int _waitTime;
        private int _focusStep;
        private int _photoCount;
        private int _focusValue;
        private int _focusCounter;
        private string _counterMessage;
        private bool _freezeImage;
        private bool _lockA;
        private bool _lockB;
        private int _selectedFocusValue;
        private bool _delayedStart;

        public ICameraDevice CameraDevice
        {
            get { return _cameraDevice; }
            set
            {
                _cameraDevice = value;
                RaisePropertyChanged(() => CameraDevice);
            }
        }

        public CameraProperty CameraProperty
        {
            get { return _cameraProperty; }
            set
            {
                _cameraProperty = value;
                RaisePropertyChanged(() => CameraProperty);
            }
        }

        public BitmapSource Bitmap
        {
            get { return _bitmap; }
            set
            {
                _bitmap = value;
                RaisePropertyChanged(() => Bitmap);
            }
        }

        public int Fps
        {
            get { return _fps; }
            set
            {
                _fps = value;
                RaisePropertyChanged(() => Fps);
            }
        }

        public string RecButtonText
        {
            get { return _recButtonText; }
            set
            {
                _recButtonText = value;
                RaisePropertyChanged(() => RecButtonText);
            }
        }

        public bool Recording
        {
            get { return _recording; }
            set
            {
                _recording = value;
                RecButtonText = _recording
                    ? TranslationStrings.ButtonRecordStopMovie
                    : TranslationStrings.ButtonRecordMovie;
                RaisePropertyChanged(() => Recording);
            }
        }

        public int GridType
        {
            get { return _gridType; }
            set
            {
                _gridType = value;
                RaisePropertyChanged(() => GridType);
            }
        }

        public AsyncObservableCollection<string> Overlays
        {
            get { return _overlays; }
            set
            {
                _overlays = value;
                RaisePropertyChanged(() => Overlays);
            }
        }

        public string SelectedOverlay
        {
            get { return CameraProperty.LiveviewSettings.SelectedOverlay; }
            set
            {
                CameraProperty.LiveviewSettings.SelectedOverlay = value;
                RaisePropertyChanged(() => SelectedOverlay);
            }
        }

        public bool BlackAndWhite
        {
            get { return CameraProperty.LiveviewSettings.BlackAndWhite; }
            set
            {
                CameraProperty.LiveviewSettings.BlackAndWhite = value;
                RaisePropertyChanged(() => BlackAndWhite);
            }
        }

        public bool EdgeDetection
        {
            get { return CameraProperty.LiveviewSettings.EdgeDetection; }
            set
            {
                CameraProperty.LiveviewSettings.EdgeDetection = value;
                RaisePropertyChanged(() => EdgeDetection);
            }
        }

        public int RotationIndex
        {
            get { return CameraProperty.LiveviewSettings.RotationIndex; }
            set
            {
                CameraProperty.LiveviewSettings.RotationIndex = value;
                RaisePropertyChanged(() => RotationIndex);
            }
        }

        public bool FreezeImage
        {
            get { return _freezeImage; }
            set
            {
                _freezeImage = value;
                if (_freezeImage)
                    _freezeTimer.Start();
                RaisePropertyChanged(() => FreezeImage);

            }
        }



        #region motion detection

        public double CurrentMotionIndex
        {
            get { return _currentMotionIndex; }
            set
            {
                _currentMotionIndex = value;
                RaisePropertyChanged(() => CurrentMotionIndex);
            }
        }

        public int MotionThreshold
        {
            get { return CameraProperty.LiveviewSettings.MotionThreshold; }
            set
            {
                CameraProperty.LiveviewSettings.MotionThreshold = value;
                RaisePropertyChanged(() => MotionThreshold);
            }
        }

        public bool TriggerOnMotion
        {
            get { return _triggerOnMotion; }
            set
            {
                _triggerOnMotion = value;
                RaisePropertyChanged(() => TriggerOnMotion);
            }
        }

        public int WaitForMotionSec
        {
            get { return CameraProperty.LiveviewSettings.WaitForMotionSec; }
            set { CameraProperty.LiveviewSettings.WaitForMotionSec = value; }
        }

        public bool MotionAutofocusBeforCapture
        {
            get { return CameraProperty.LiveviewSettings.MotionAutofocusBeforCapture; }
            set { CameraProperty.LiveviewSettings.MotionAutofocusBeforCapture = value; }
        }

        public bool DetectMotion
        {
            get { return CameraProperty.LiveviewSettings.DetectMotion; }
            set { CameraProperty.LiveviewSettings.DetectMotion = value; }
        }

        #endregion

        #region histogram

        public PointCollection LuminanceHistogramPoints
        {
            get { return _luminanceHistogramPoints; }
            set
            {
                if (_luminanceHistogramPoints != value)
                {
                    _luminanceHistogramPoints = value;
                    RaisePropertyChanged(() => LuminanceHistogramPoints);
                }
            }
        }

        public bool HighlightOverExp
        {
            get { return CameraProperty.LiveviewSettings.HighlightOverExp; }
            set
            {
                CameraProperty.LiveviewSettings.HighlightOverExp = value;
                RaisePropertyChanged(() => HighlightOverExp);
            }
        }

        public bool HighlightUnderExp
        {
            get { return CameraProperty.LiveviewSettings.HighlightUnderExp; }
            set
            {
                CameraProperty.LiveviewSettings.HighlightUnderExp = value;
                RaisePropertyChanged(() => HighlightUnderExp);
            }
        }

        #endregion

        #region focus stacking

        public bool IsFocusStackingRunning
        {
            get { return _isBusy; }
            set
            {
                _isBusy = value;
                RaisePropertyChanged(() => IsFocusStackingRunning);
                RaisePropertyChanged(() => IsFree);
                RaisePropertyChanged(() => FocusingEnabled);
            }
        }

        public bool IsFree
        {
            get { return !_isBusy; }
        }


        public int PhotoNo
        {
            get { return _photoNo; }
            set
            {
                _photoNo = value;
                RaisePropertyChanged(() => PhotoNo);
                if (PhotoNo > 0)
                    _focusStep =
                        Convert.ToInt32(Decimal.Round((decimal) FocusValue/PhotoNo, MidpointRounding.AwayFromZero));
                RaisePropertyChanged(() => FocusStep);
                RaisePropertyChanged(() => PhotoNo);
            }
        }

        public int WaitTime
        {
            get { return _waitTime; }
            set
            {
                _waitTime = value;
                RaisePropertyChanged(() => WaitTime);
            }
        }

        public int FocusStep
        {
            get { return _focusStep; }
            set
            {
                _focusStep = value;
                _photoNo = Convert.ToInt32(Decimal.Round((decimal) FocusValue/FocusStep, MidpointRounding.AwayFromZero));
                RaisePropertyChanged(() => FocusStep);
                RaisePropertyChanged(() => PhotoNo);
            }
        }

        public int PhotoCount
        {
            get { return _photoCount; }
            set
            {
                _photoCount = value;
                RaisePropertyChanged(() => PhotoCount);
            }
        }

        /// <summary>
        /// Gets or sets the current focus counter.
        /// </summary>
        /// <value>
        /// The focus counter.
        /// </value>
        public int FocusCounter
        {
            get { return _focusCounter; }
            set
            {
                _selectedFocusValue = value;
                _focusCounter = value;

                RaisePropertyChanged(() => FocusCounter);
                RaisePropertyChanged(() => CounterMessage);
                RaisePropertyChanged(() => SelectedFocusValue);
            }
        }

        /// <summary>
        /// Gets or sets the maximum locked focus value.
        /// </summary>
        /// <value>
        /// The focus value.
        /// </value>
        public int FocusValue
        {
            get { return _focusValue; }
            set
            {
                _focusValue = value;
                if (FocusStep > 0)
                    PhotoNo = FocusValue/FocusStep;
                RaisePropertyChanged(() => FocusValue);
                RaisePropertyChanged(() => CounterMessage);
            }
        }

        public int SelectedFocusValue
        {
            get { return _selectedFocusValue; }
            set
            {
                var newvalue = value;
                if (_selectedFocusValue != newvalue)
                {
                    SetFocus(newvalue - _selectedFocusValue);
                    _selectedFocusValue = value;
                    RaisePropertyChanged(() => SelectedFocusValue);
                }
            }
        }


        public string CounterMessage
        {
            get
            {
                if (!LockA && !LockB)
                    return "?";
                if (LockA && !LockB)
                    return FocusCounter.ToString();
                if (LockB)
                    return FocusCounter + "/" + FocusValue;
                return _counterMessage;
            }
            set
            {
                _counterMessage = value;
                RaisePropertyChanged(() => CounterMessage);
            }
        }

        public bool LockA
        {
            get { return _lockA; }
            set
            {
                _lockA = value;
                if (_lockA && !LockB)
                {
                    FocusCounter = 0;
                    FocusValue = 0;
                    LockB = false;
                }
                if (_lockA && LockB)
                {
                    FocusValue = FocusValue - FocusCounter;
                    FocusCounter = 0;
                }
                if (!_lockA)
                {
                    LockB = false;
                }
                RaisePropertyChanged(() => LockA);
                RaisePropertyChanged(() => CounterMessage);
            }
        }

        public bool LockB
        {
            get { return _lockB; }
            set
            {
                _lockB = value;
                if (_lockB)
                {
                    FocusValue = FocusCounter;
                    PhotoCount = 5;
                }
                RaisePropertyChanged(() => LockB);
                RaisePropertyChanged(() => CounterMessage);
            }
        }

        public bool FocusingEnabled
        {
            get { return !IsFocusStackingRunning && !_focusIProgress; }
        }

        #endregion

        #region view

        public bool ShowFocusRect
        {
            get { return CameraProperty.LiveviewSettings.ShowFocusRect; }
            set
            {
                CameraProperty.LiveviewSettings.ShowFocusRect = value;
                RaisePropertyChanged(() => ShowFocusRect);
            }
        }

        /// <summary>
        /// Gets or sets a value indicating if restart timer is running.
        /// </summary>
        /// <value>
        ///   <c>true</c> if [delayed start]; otherwise, <c>false</c>.
        /// </value>
        public bool DelayedStart
        {
            get { return _delayedStart; }
            set
            {
                _delayedStart = value;
                RaisePropertyChanged(() => DelayedStart);
            }
        }

        #endregion
        public BitmapSource Preview
        {
            get { return _preview; }
            set
            {
                _preview = value;
                RaisePropertyChanged(() => Preview);
            }
        }


        public LiveViewData LiveViewData { get; set; }

        #region Commands

        public RelayCommand AutoFocusCommand { get; set; }
        public RelayCommand RecordMovieCommand { get; set; }
        public RelayCommand CaptureCommand { get; set; }
        public RelayCommand FocusPCommand { get; set; }
        public RelayCommand FocusPPCommand { get; set; }
        public RelayCommand FocusPPPCommand { get; set; }
        public RelayCommand FocusMCommand { get; set; }
        public RelayCommand FocusMMCommand { get; set; }
        public RelayCommand FocusMMMCommand { get; set; }
        public RelayCommand MoveACommand { get; set; }
        public RelayCommand MoveBCommand { get; set; }
        public RelayCommand StartFocusStackingCommand { get; set; }
        public RelayCommand PreviewFocusStackingCommand { get; set; }
        public RelayCommand StopFocusStackingCommand { get; set; }
        
        
        #endregion

        public LiveViewViewModel()
        {
            CameraProperty = new CameraProperty();
            CameraDevice = new NotConnectedCameraDevice();
            InitCommands();
        }

        public LiveViewViewModel(ICameraDevice device)
        {
            CameraDevice = device;
            CameraProperty = device.LoadProperties();
            InitOverlay();
            InitCommands();
            if (ServiceProvider.Settings.DetectionType == 0)
            {
                _detector = new MotionDetector(
                    new TwoFramesDifferenceDetector(true),
                    new BlobCountingObjectsProcessing(
                        ServiceProvider.Settings.MotionBlockSize,
                        ServiceProvider.Settings.MotionBlockSize, true));
            }
            else
            {
                _detector = new MotionDetector(
                    new SimpleBackgroundModelingDetector(true, true),
                    new BlobCountingObjectsProcessing(
                        ServiceProvider.Settings.MotionBlockSize,
                        ServiceProvider.Settings.MotionBlockSize, true));
            }
            TriggerOnMotion = false;
            Init();
        }

        private void InitCommands()
        {
            AutoFocusCommand = new RelayCommand(AutoFocus);
            RecordMovieCommand = new RelayCommand(RecordMovie,
                () => CameraDevice.GetCapability(CapabilityEnum.RecordMovie));
            CaptureCommand = new RelayCommand(Capture);
            FocusMCommand = new RelayCommand(() => SetFocus(-ServiceProvider.Settings.SmalFocusStep));
            FocusMMCommand = new RelayCommand(() => SetFocus(-ServiceProvider.Settings.MediumFocusStep));
            FocusMMMCommand = new RelayCommand(() => SetFocus(-ServiceProvider.Settings.LargeFocusStep));
            FocusPCommand = new RelayCommand(() => SetFocus(ServiceProvider.Settings.SmalFocusStep));
            FocusPPCommand = new RelayCommand(() => SetFocus(ServiceProvider.Settings.MediumFocusStep));
            FocusPPPCommand = new RelayCommand(() => SetFocus(ServiceProvider.Settings.LargeFocusStep));
            MoveACommand = new RelayCommand(() => SetFocus(-FocusCounter));
            MoveBCommand = new RelayCommand(() => SetFocus(FocusValue));
            StartFocusStackingCommand = new RelayCommand(StartFocusStacking, () => LockB);
            PreviewFocusStackingCommand = new RelayCommand(PreviewFocusStacking, () => LockB);
            StopFocusStackingCommand=new RelayCommand(StopFocusStacking);
        }

        private void InitOverlay()
        {
            Overlays = new AsyncObservableCollection<string>();
            Overlays.Add(TranslationStrings.LabelNone);
            Overlays.Add(TranslationStrings.LabelRuleOfThirds);
            Overlays.Add(TranslationStrings.LabelComboGrid);
            Overlays.Add(TranslationStrings.LabelDiagonal);
            Overlays.Add(TranslationStrings.LabelSplit);
            if (Directory.Exists(ServiceProvider.Settings.OverlayFolder))
            {
                string[] files = Directory.GetFiles(ServiceProvider.Settings.OverlayFolder, "*.png");
                foreach (string file in files)
                {
                    Overlays.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
        }

        private void Init()
        {
            WaitTime = 2;
            PhotoNo = 2;
            FocusStep = 2;
            DelayedStart = false;
            _timer.Stop();
            _timer.AutoReset = true;
            CameraDevice.CameraDisconnected += CameraDeviceCameraDisconnected;
            _photoCapturedTime = DateTime.Now;
            CameraDevice.PhotoCaptured += CameraDevicePhotoCaptured;
            StartLiveView();
            _freezeTimer.Interval = ServiceProvider.Settings.LiveViewFreezeTimeOut*1000;
            _freezeTimer.Elapsed += _freezeTimer_Elapsed;
            _timer.Elapsed += _timer_Elapsed;
            _worker.DoWork += delegate
            {
                if (!FreezeImage)
                    GetLiveImage();
            };
            ServiceProvider.WindowsManager.Event += WindowsManager_Event;
            _focusStackingTimer.AutoReset = true;
            _focusStackingTimer.Elapsed += _focusStackingTimer_Elapsed;
            _restartTimer.AutoReset = true;
            _restartTimer.Elapsed += _restartTimer_Elapsed;
        }

        private void _restartTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if ((DateTime.Now - _restartTimerStartTime).TotalSeconds > 2)
            {
                _restartTimer.Stop();
                StartLiveView();
                DelayedStart = false;
            }
        }

        public void UnInit()
        {
            _timer.Stop();
            _focusStackingTimer.Stop();
            _restartTimer.Stop();
            CameraDevice.PhotoCaptured -= CameraDevicePhotoCaptured; 
            Thread.Sleep(100);
            StopLiveView();
            Recording = false;
            LockA = false;
            LockB = false;
            LiveViewData = null;
        }

        private void WindowsManager_Event(string cmd, object o)
        {
            ICameraDevice device = o as ICameraDevice ?? ServiceProvider.DeviceManager.SelectedCameraDevice;
            if (device != CameraDevice)
                return;

            switch (cmd)
            {
                case CmdConsts.LiveView_Capture:
                    Capture();
                    break;
                case CmdConsts.LiveView_Focus_Move_Right:
                    if (LiveViewData != null && LiveViewData.ImageData != null)
                    {
                        SetFocusPos(LiveViewData.FocusX + ServiceProvider.Settings.FocusMoveStep, LiveViewData.FocusY);
                    }
                    break;
                case CmdConsts.LiveView_Focus_Move_Left:
                    if (LiveViewData != null && LiveViewData.ImageData != null)
                    {
                        SetFocusPos(LiveViewData.FocusX - ServiceProvider.Settings.FocusMoveStep, LiveViewData.FocusY);
                    }
                    break;
                case CmdConsts.LiveView_Focus_Move_Up:
                    if (LiveViewData != null && LiveViewData.ImageData != null)
                    {
                        SetFocusPos(LiveViewData.FocusX, LiveViewData.FocusY - ServiceProvider.Settings.FocusMoveStep);
                    }
                    break;
                case CmdConsts.LiveView_Focus_Move_Down:
                    if (LiveViewData != null && LiveViewData.ImageData != null)
                    {
                        SetFocusPos(LiveViewData.FocusX, LiveViewData.FocusY + ServiceProvider.Settings.FocusMoveStep);
                    }
                    break;
                case CmdConsts.LiveView_Zoom_All:
                    CameraDevice.LiveViewImageZoomRatio.SetValue(0);
                    break;
                case CmdConsts.LiveView_Zoom_25:
                    CameraDevice.LiveViewImageZoomRatio.SetValue(1);
                    break;
                case CmdConsts.LiveView_Zoom_33:
                    CameraDevice.LiveViewImageZoomRatio.SetValue(2);
                    break;
                case CmdConsts.LiveView_Zoom_50:
                    CameraDevice.LiveViewImageZoomRatio.SetValue(3);
                    break;
                case CmdConsts.LiveView_Zoom_66:
                    CameraDevice.LiveViewImageZoomRatio.SetValue(4);
                    break;
                case CmdConsts.LiveView_Zoom_100:
                    CameraDevice.LiveViewImageZoomRatio.SetValue(5);
                    break;
                case CmdConsts.LiveView_Focus_M:
                    FocusMCommand.Execute(null);
                    break;
                case CmdConsts.LiveView_Focus_P:
                    FocusPCommand.Execute(null);
                    break;
                case CmdConsts.LiveView_Focus_MM:
                    FocusMMCommand.Execute(null);
                    break;
                case CmdConsts.LiveView_Focus_PP:
                    FocusPPCommand.Execute(null);
                    break;
                case CmdConsts.LiveView_Focus_MMM:
                    FocusMMMCommand.Execute(null);
                    break;
                case CmdConsts.LiveView_Focus_PPP:
                    FocusPPPCommand.Execute(null);
                    break;
                case CmdConsts.LiveView_Focus:
                    AutoFocus();
                    break;

            }
        }

        void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!_worker.IsBusy)
                _worker.RunWorkerAsync();
        }

        void _freezeTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            FreezeImage = false;
        }

        private void CameraDevicePhotoCaptured(object sender, PhotoCapturedEventArgs eventArgs)
        {
            _detector.Reset();
            _photoCapturedTime = DateTime.Now;
            if (IsFocusStackingRunning)
            {
                _focusStackingTimer.Start();
            }
            _timer.Start();
            StartLiveView();
        }

        private void CameraDeviceCameraDisconnected(object sender, DisconnectCameraEventArgs eventArgs)
        {
            ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.LiveViewWnd_Hide, CameraDevice);
        }

        private void AutoFocus()
        {
            string resp = CameraDevice.GetProhibitionCondition(OperationEnum.AutoFocus);
            if (string.IsNullOrEmpty(resp))
            {
                Thread thread = new Thread(AutoFocusThread);
                thread.Start();
            }
            else
            {
                MessageBox.Show(TranslationStrings.LabelError,
                    TranslationStrings.LabelErrorUnableFocus + "\n" +
                    TranslationManager.GetTranslation(resp));
            }
        }

        private void AutoFocusThread()
        {
            try
            {
                LockB = false;
                LockA = false;
                CameraDevice.AutoFocus();
            }
            catch (Exception exception)
            {
                Log.Error("Unable to autofocus", exception);
                StaticHelper.Instance.SystemMessage = exception.Message;
            }
        }

   
        private void GetLiveImage()
        {
            if (_operInProgress)
                return;
            
            if (DelayedStart)
                return;

            _operInProgress = true;
            _totalframes++;
            if ((DateTime.Now - _framestart).TotalSeconds > 0)
                Fps = (int) (_totalframes/(DateTime.Now - _framestart).TotalSeconds);
            try
            {
                LiveViewData = LiveViewManager.GetLiveViewImage(CameraDevice);
            }
            catch (Exception)
            {
                _retries++;
                _operInProgress = false;
                return;
            }

            if (LiveViewData == null )
            {
                _retries++;
                _operInProgress = false;
                return;
            }

            if (!LiveViewData.IsLiveViewRunning)
            {
                DelayedStart = true;
                _restartTimerStartTime = DateTime.Now;
                _restartTimer. Start();
                _operInProgress = false;
                return;
            }

            if (LiveViewData.ImageData == null)
            {
                _retries++;
                _operInProgress = false;
                return;
            }

            Recording = LiveViewData.MovieIsRecording;
            try
            {
                WriteableBitmap preview;
                if (LiveViewData != null && LiveViewData.ImageData != null)
                {
                    MemoryStream stream = new MemoryStream(LiveViewData.ImageData,
                        LiveViewData.
                            ImageDataPosition,
                        LiveViewData.ImageData.
                            Length -
                        LiveViewData.
                            ImageDataPosition);

                    using (var bmp = new Bitmap(stream))
                    {
                        if (DetectMotion)
                        {
                            ProcessMotionDetection(bmp);
                        }

                        if (_totalframes%DesiredFrameRate == 0)
                        {
                            ImageStatisticsHSL hslStatistics =
                                new ImageStatisticsHSL(bmp);
                            LuminanceHistogramPoints =
                                ConvertToPointCollection(
                                    hslStatistics.Luminance.Values);
                        }

                        if (HighlightUnderExp)
                        {
                            ColorFiltering filtering = new ColorFiltering();
                            filtering.Blue = new IntRange(0, 5);
                            filtering.Red = new IntRange(0, 5);
                            filtering.Green = new IntRange(0, 5);
                            filtering.FillOutsideRange = false;
                            filtering.FillColor = new RGB(System.Drawing.Color.Blue);
                            filtering.ApplyInPlace(bmp);
                        }

                        if (HighlightOverExp)
                        {
                            ColorFiltering filtering = new ColorFiltering();
                            filtering.Blue = new IntRange(250, 255);
                            filtering.Red = new IntRange(250, 255);
                            filtering.Green = new IntRange(250, 255);
                            filtering.FillOutsideRange = false;
                            filtering.FillColor = new RGB(System.Drawing.Color.Red);
                            filtering.ApplyInPlace(bmp);
                        }
                        preview =
                            BitmapFactory.ConvertToPbgra32Format(
                                BitmapSourceConvert.ToBitmapSource(bmp));
                        DrawFocusPoint(preview);
                        Bitmap newbmp = bmp;
                        if (EdgeDetection)
                        {
                            var filter = new FiltersSequence(
                                Grayscale.CommonAlgorithms.BT709,
                                new HomogenityEdgeDetector()
                                );
                            newbmp = filter.Apply(bmp);
                        }

                        WriteableBitmap writeableBitmap;

                        if (BlackAndWhite)
                        {
                            Grayscale filter = new Grayscale(0.299, 0.587, 0.114);
                            writeableBitmap =
                                BitmapFactory.ConvertToPbgra32Format(
                                    BitmapSourceConvert.ToBitmapSource(
                                        filter.Apply(newbmp)));
                        }
                        else
                        {
                            writeableBitmap =
                                BitmapFactory.ConvertToPbgra32Format(
                                    BitmapSourceConvert.ToBitmapSource(newbmp));
                        }
                        DrawGrid(writeableBitmap);
                        if (RotationIndex != 0)
                        {
                            switch (RotationIndex)
                            {
                                case 1:
                                    writeableBitmap = writeableBitmap.Rotate(90);
                                    break;
                                case 2:
                                    writeableBitmap = writeableBitmap.Rotate(180);
                                    break;
                                case 3:
                                    writeableBitmap = writeableBitmap.Rotate(270);
                                    break;
                                case 4:
                                    if (LiveViewData.Rotation != 0)
                                        writeableBitmap =
                                            writeableBitmap.RotateFree(
                                                LiveViewData.Rotation, false);
                                    break;
                            }
                        }
                        if (ShowFocusRect)
                            DrawFocusPoint(writeableBitmap);

                        writeableBitmap.Freeze();
                        Bitmap = writeableBitmap;

                        ServiceProvider.DeviceManager.LiveViewImage[CameraDevice] = SaveJpeg(writeableBitmap);
                    }
                    if (CameraDevice.LiveViewImageZoomRatio.Value == "All")
                    {
                        preview.Freeze();
                        Preview = preview;
                    }
                    stream.Close();
                }
            }
            catch (Exception exception)
            {
                Log.Error(exception);
                _retries++;
                _operInProgress = false;
            }
            _retries = 0;
            _operInProgress = false;
        }

        public byte[] SaveJpeg(WriteableBitmap image)
        {
            var enc = new JpegBitmapEncoder();
            enc.QualityLevel = 90;
            enc.Frames.Add(BitmapFrame.Create(image));

            using (MemoryStream stm = new MemoryStream())
            {
                enc.Save(stm);
                return stm.ToArray();
            }
        }

        private void DrawGrid(WriteableBitmap writeableBitmap)
        {
            Color color = Colors.White;
            color.A = 50;
            switch (GridType)
            {
                case 1:
                {
                    for (int i = 1; i < 3; i++)
                    {
                        writeableBitmap.DrawLine(0, (int) ((writeableBitmap.Height/3)*i),
                            (int) writeableBitmap.Width,
                            (int) ((writeableBitmap.Height/3)*i), color);
                        writeableBitmap.DrawLine((int) ((writeableBitmap.Width/3)*i), 0,
                            (int) ((writeableBitmap.Width/3)*i),
                            (int) writeableBitmap.Height, color);
                    }
                    writeableBitmap.SetPixel((int) (writeableBitmap.Width/2), (int) (writeableBitmap.Height/2), 128,
                        Colors.Red);
                }
                    break;
                case 2:
                {
                    for (int i = 1; i < 10; i++)
                    {
                        writeableBitmap.DrawLine(0, (int) ((writeableBitmap.Height/10)*i),
                            (int) writeableBitmap.Width,
                            (int) ((writeableBitmap.Height/10)*i), color);
                        writeableBitmap.DrawLine((int) ((writeableBitmap.Width/10)*i), 0,
                            (int) ((writeableBitmap.Width/10)*i),
                            (int) writeableBitmap.Height, color);
                    }
                    writeableBitmap.SetPixel((int) (writeableBitmap.Width/2), (int) (writeableBitmap.Height/2), 128,
                        Colors.Red);
                }
                    break;
                case 3:
                {
                    writeableBitmap.DrawLineDDA(0, 0, (int) writeableBitmap.Width,
                        (int) writeableBitmap.Height, color);

                    writeableBitmap.DrawLineDDA(0, (int) writeableBitmap.Height,
                        (int) writeableBitmap.Width, 0, color);
                    writeableBitmap.SetPixel((int) (writeableBitmap.Width/2), (int) (writeableBitmap.Height/2), 128,
                        Colors.Red);
                }
                    break;
                case 4:
                {
                    writeableBitmap.DrawLineDDA(0, (int) (writeableBitmap.Height/2), (int) writeableBitmap.Width,
                        (int) (writeableBitmap.Height/2), color);

                    writeableBitmap.DrawLineDDA((int) (writeableBitmap.Width/2), 0,
                        (int) (writeableBitmap.Width/2), (int) writeableBitmap.Height, color);
                    writeableBitmap.SetPixel((int) (writeableBitmap.Width/2), (int) (writeableBitmap.Height/2), 128,
                        Colors.Red);
                }
                    break;
                default:
                    try
                    {
                        if (GridType > 4)
                        {
                            string filename = Path.Combine(ServiceProvider.Settings.OverlayFolder,
                                SelectedOverlay + ".png");
                            if (File.Exists(filename))
                            {
                                BitmapImage bitmapSource = new BitmapImage();
                                bitmapSource.BeginInit();
                                bitmapSource.UriSource = new Uri(filename);
                                bitmapSource.EndInit();
                                WriteableBitmap overlay = BitmapFactory.ConvertToPbgra32Format(bitmapSource);
                                writeableBitmap.Blit(
                                    new Rect(0, 0, writeableBitmap.PixelWidth, writeableBitmap.PixelHeight),
                                    overlay,
                                    new Rect(0, 0, overlay.PixelWidth, overlay.PixelHeight));
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                    break;
            }
        }

        private void DrawFocusPoint(WriteableBitmap bitmap)
        {
            try
            {
                if (LiveViewData == null)
                    return;
                double xt = bitmap.Width / LiveViewData.ImageWidth;
                double yt = bitmap.Height / LiveViewData.ImageHeight;

                bitmap.DrawRectangle((int) (LiveViewData.FocusX*xt - (LiveViewData.FocusFrameXSize*xt/2)),
                    (int) (LiveViewData.FocusY*yt - (LiveViewData.FocusFrameYSize*yt/2)),
                    (int) (LiveViewData.FocusX*xt + (LiveViewData.FocusFrameXSize*xt/2)),
                    (int) (LiveViewData.FocusY*yt + (LiveViewData.FocusFrameYSize*yt/2)),
                    LiveViewData.HaveFocusData ? Colors.Green : Colors.Red);
            }
            catch (Exception exception)
            {
                Log.Error("Error draw helper lines", exception);
            }
        }


        private void ProcessMotionDetection(Bitmap bmp)
        {
            try
            {
                float movement = _detector.ProcessFrame(bmp);
                CurrentMotionIndex = Math.Round(movement*100, 2);
                if (movement > ((float) MotionThreshold/100) && TriggerOnMotion &&
                    (DateTime.Now - _photoCapturedTime).TotalSeconds > WaitForMotionSec)
                {
                    if (MotionAutofocusBeforCapture)
                    {
                        BlobCountingObjectsProcessing processing =
                            _detector.MotionProcessingAlgorithm as BlobCountingObjectsProcessing;
                        if (processing != null && processing.ObjectRectangles != null &&
                            processing.ObjectRectangles.Length > 0 &&
                            LiveViewData.ImageData != null)
                        {
                            System.Drawing.Rectangle rectangle = new System.Drawing.Rectangle();
                            int surface = 0;
                            foreach (System.Drawing.Rectangle objectRectangle in processing.ObjectRectangles)
                            {
                                if (surface < objectRectangle.Width*objectRectangle.Height)
                                {
                                    surface = objectRectangle.Width*objectRectangle.Height;
                                    rectangle = objectRectangle;
                                }
                            }
                            double xt = LiveViewData.ImageWidth/(double) bmp.Width;
                            double yt = LiveViewData.ImageHeight/(double) bmp.Height;
                            int posx = (int) ((rectangle.X + (rectangle.Width/2))*xt);
                            int posy = (int) ((rectangle.Y + (rectangle.Height/2))*yt);
                            CameraDevice.Focus(posx, posy);
                        }
                        AutoFocus();
                    }
                    CameraDevice.CapturePhotoNoAf();
                    _detector.Reset();
                    _photoCapturedTime = DateTime.Now;
                }
            }
            catch (Exception exception)
            {
                Log.Error("Motion detection error ", exception);
            }
        }

        private PointCollection ConvertToPointCollection(int[] values)
        {
            int max = values.Max();

            PointCollection points = new PointCollection();
            // first point (lower-left corner)
            points.Add(new Point(0, max));
            // middle points
            for (int i = 0; i < values.Length; i++)
            {
                points.Add(new Point(i, max - values[i]));
            }
            // last point (lower-right corner)
            points.Add(new Point(values.Length - 1, max));
            points.Freeze();
            return points;
        }

        private void RecordMovie()
        {
            string resp = Recording ? "" : CameraDevice.GetProhibitionCondition(OperationEnum.RecordMovie);
            if (string.IsNullOrEmpty(resp))
            {
                var thread = new Thread(RecordMovieThread);
                thread.Start();
            }
            else
            {
                MessageBox.Show(TranslationStrings.LabelError,
                    TranslationStrings.LabelErrorRecordMovie + "\n" +
                    TranslationManager.GetTranslation(resp));
            }
        }

        private void RecordMovieThread()
        {
            try
            {
                if (Recording)
                {
                    CameraDevice.StopRecordMovie();
                }
                else
                {
                    CameraDevice.StartRecordMovie();
                }
            }
            catch (Exception exception)
            {
                StaticHelper.Instance.SystemMessage = exception.Message;
                Log.Error("Recording error", exception);
            }
        }

        private void StartLiveView()
        {
            string resp = CameraDevice.GetProhibitionCondition(OperationEnum.LiveView);
            if (string.IsNullOrEmpty(resp))
            {
                Thread thread = new Thread(StartLiveViewThread);
                thread.Start();
                thread.Join();
            }
            else
            {
                Log.Error("Error starting live view " + resp);
                MessageBox.Show(TranslationStrings.LabelError,
                    TranslationStrings.LabelLiveViewError + "\n" +
                    TranslationManager.GetTranslation(resp));
            }
        }

        private void StartLiveViewThread()
        {
            try
            {
                _totalframes = 0;
                _framestart = DateTime.Now;
                bool retry = false;
                int retryNum = 0;
                Log.Debug("LiveView: Liveview started");
                do
                {
                    try
                    {
                        LiveViewManager.StartLiveView(CameraDevice);
                    }
                    catch (DeviceException deviceException)
                    {
                        if (deviceException.ErrorCode == ErrorCodes.ERROR_BUSY ||
                            deviceException.ErrorCode == ErrorCodes.MTP_Device_Busy)
                        {
                            Thread.Sleep(100);
                            Log.Debug("Retry live view :" + deviceException.ErrorCode.ToString("X"));
                            retry = true;
                            retryNum++;
                        }
                        else
                        {
                            throw;
                        }
                    }
                } while (retry && retryNum < 35);
                _timer.Start();
                _operInProgress = false;
                _retries = 0;
                Log.Debug("LiveView: Liveview start done");
            }
            catch (Exception exception)
            {
                Log.Error("Unable to start liveview !", exception);
                StaticHelper.Instance.SystemMessage = "Unable to start liveview ! " + exception.Message;
            }
        }

        private void StopLiveView()
        {
            Thread thread = new Thread(StopLiveViewThread);
            thread.Start();
        }

        private void StopLiveViewThread()
        {
            try
            {
                _totalframes = 0;
                _framestart = DateTime.Now;
                bool retry = false;
                int retryNum = 0;
                Log.Debug("LiveView: Liveview stopping");
                do
                {
                    try
                    {
                        LiveViewManager.StopLiveView(CameraDevice);
                    }
                    catch (DeviceException deviceException)
                    {
                        if (deviceException.ErrorCode == ErrorCodes.ERROR_BUSY ||
                            deviceException.ErrorCode == ErrorCodes.MTP_Device_Busy)
                        {
                            Thread.Sleep(500);
                            Log.Debug("Retry live view stop:" + deviceException.ErrorCode.ToString("X"));
                            retry = true;
                            retryNum++;
                        }
                        else
                        {
                            throw;
                        }
                    }
                } while (retry && retryNum < 35);
            }
            catch (Exception exception)
            {
                Log.Error("Unable to stop liveview !", exception);
                StaticHelper.Instance.SystemMessage = "Unable to stop liveview ! " + exception.Message;
            }
        }

        private void SetFocus(int step)
        {
            if (step == 0)
                return;

            if (_focusIProgress)
            {
                SelectedFocusValue = FocusCounter;
                return;
            }
            _focusIProgress = true;
            RaisePropertyChanged(() => FocusingEnabled);
            try
            {
                string resp = CameraDevice.GetProhibitionCondition(OperationEnum.ManualFocus);
                if (string.IsNullOrEmpty(resp))
                {
                    Thread thread = new Thread(SetFocusThread);
                    thread.Start(step);
                    thread.Join();
                }
                else
                {
                    MessageBox.Show(TranslationStrings.LabelError,
                                          TranslationStrings.LabelErrorUnableFocus + "\n" +
                                          TranslationManager.GetTranslation(resp));
                    _focusIProgress = false;
                    RaisePropertyChanged(() => FocusingEnabled);
                    SelectedFocusValue = FocusCounter;
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(TranslationStrings.LabelError, TranslationStrings.LabelErrorUnableFocus);
                Log.Error("Unable to focus ", exception);
                _focusIProgress = false;
                RaisePropertyChanged(() => FocusingEnabled);
                SelectedFocusValue = FocusCounter;
            }
        }

        private void SetFocusThread(object ostep)
        {
            int step = (int)ostep;
            if (LockA)
            {
                if (FocusCounter == 0 && step < 0)
                    return;
                if (FocusCounter + step < 0)
                    step = -FocusCounter;
            }
            if (LockB)
            {
                if (FocusCounter + step > FocusValue)
                    step = FocusValue - FocusCounter;
            }

            try
            {
                _timer.Stop();
                CameraDevice.StartLiveView();
                StaticHelper.Instance.SystemMessage = "Move focus " + step;
                int stepdone = CameraDevice.Focus(step);
                FocusCounter += stepdone;
            }
            catch (DeviceException exception)
            {
                Log.Error("Unable to focus", exception);
                StaticHelper.Instance.SystemMessage = TranslationStrings.LabelErrorUnableFocus + " " + exception.Message;
            }
            catch (Exception exception)
            {
                Log.Error("Unable to focus", exception);
                StaticHelper.Instance.SystemMessage = TranslationStrings.LabelErrorUnableFocus;
            }

            if (!IsFocusStackingRunning)
                _timer.Start();

            _focusIProgress = false;
            RaisePropertyChanged(() => FocusingEnabled);
        }

        private void Capture()
        {
            Log.Debug("LiveView: Capture started");
            _timer.Stop();
            Thread.Sleep(300);
            try
            {
                if (CameraDevice.ShutterSpeed != null && CameraDevice.ShutterSpeed.Value == "Bulb")
                {
                    StaticHelper.Instance.SystemMessage = TranslationStrings.MsgBulbModeNotSupported;
                    MessageBox.Show(TranslationStrings.LabelError, TranslationStrings.MsgBulbModeNotSupported);
                    return;
                }
                CameraDevice.CapturePhotoNoAf();
                Log.Debug("LiveView: Capture Initialization Done");
            }
            catch (DeviceException exception)
            {
                StaticHelper.Instance.SystemMessage = exception.Message;
                Log.Error("Unable to take picture with no af", exception);
            }
        }

        public void SetFocusPos(Point initialPoint, double refWidth, double refHeight)
        {
            if (LiveViewData != null)
            {
                double xt = LiveViewData.ImageWidth/refWidth;
                double yt = LiveViewData.ImageHeight/refHeight;
                int posx = (int) (initialPoint.X*xt);
                int posy = (int) (initialPoint.Y*yt);
                SetFocusPos(posx, posy);
            }
        }

        private void SetFocusPos(int x, int y)
        {
            try
            {
                CameraDevice.Focus(x, y);
            }
            catch (Exception exception)
            {
                Log.Error("Error set focus pos :", exception);
                StaticHelper.Instance.SystemMessage = TranslationStrings.LabelErrorSetFocusPos;
            }
        }

        private void StartFocusStacking()
        {
            if (!LockA || !LockB)
            {
                MessageBox.Show(TranslationStrings.LabelError, TranslationStrings.LabelLockNearFar);
                return;
            }
            _focusStackingTick = 0;
            Thread.Sleep(500);
            _focusIProgress = false;
            SetFocus(-FocusCounter);
            GetLiveImage();
            PhotoCount = 0;
            IsFocusStackingRunning = true;
            _focusStackingPreview = false;
            _focusStackingTimer.Start();
            //Thread thread = new Thread(TakePhoto);
            //thread.Start();
        }

        private void PreviewFocusStacking()
        {
            SetFocus(-FocusCounter);
            //FreezeImage = true;
            GetLiveImage();
            PhotoCount = 0;
            IsFocusStackingRunning = true;
            _focusStackingPreview = true;
            _focusStackingTimer.Start();
        }

        private void StopFocusStacking()
        {
            IsFocusStackingRunning = false;
            _focusStackingTick = 0;
            _focusStackingTimer.Stop();
            _timer.Start();
        }


        void _focusStackingTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!IsFocusStackingRunning)
                return;
            if (FocusCounter >= FocusValue)
            {
                IsFocusStackingRunning = false;
            }
            if (_focusStackingTick > WaitTime)
            {
                _focusStackingTimer.Stop();
                StartLiveView();
                if (PhotoCount > 0)
                {
                    SetFocus(FocusStep);
                }
                PhotoCount++;
                GetLiveImage();
                _focusStackingTick = 0;
                if (!_focusStackingPreview)
                {
                    Recording = false;
                    try
                    {
                        var thread = new Thread(Capture);
                        thread.Start();
                        thread.Join();
                    }
                    catch (Exception exception)
                    {
                        Log.Error(exception);
                        StaticHelper.Instance.SystemMessage = exception.Message;
                        _focusStackingTimer.Start();
                    }
                }
                else
                {
                    _focusStackingTimer.Start();
                }
            }
            _focusStackingTick++;
        }
    }
}