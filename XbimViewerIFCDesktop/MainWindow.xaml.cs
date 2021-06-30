using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Xbim.Common;
using Xbim.Geometry.Engine.Interop;
using Xbim.Ifc;
using Xbim.IO;
using Xbim.ModelGeometry.Scene;
using Xbim.Presentation;
using Xbim.Presentation.XplorerPluginSystem;

namespace XbimViewerIFCDesktop
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : IXbimXplorerPluginMasterWindow, INotifyPropertyChanged
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private BackgroundWorker _loadFileBackgroundWorker;

        DrawingControl3D IXbimXplorerPluginMasterWindow.DrawingControl
        {
            get { return DrawingControl; }
        }

        public IPersistEntity SelectedItem
        {
            get { return (IPersistEntity)GetValue(SelectedItemProperty); }
            set { SetValue(SelectedItemProperty, value); }
        }

        public IfcStore Model
        {
            get
            {
                var op = MainFrame.DataContext as ObjectDataProvider;
                return op == null ? null : op.ObjectInstance as IfcStore;
            }
        }

        private ObjectDataProvider ModelProvider
        {
            get
            {
                return MainFrame.DataContext as ObjectDataProvider;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void BroadCastMessage(object sender, string messageTypeString, object messageData)
        {
            MessageBox.Show("BroadCastMessage");
        }

        public string GetAssemblyLocation(Assembly requestingAssembly)
        {
            MessageBox.Show("GetAssemblyLocation");
            return "";
        }

        public static ILoggerFactory LoggerFactory { get; private set; }
        public ILoggerFactory GetLoggerFactory()
        {
            return LoggerFactory;
        }

        private string _openedModelFileName;
        public string GetOpenedModelFileName()
        {
            return _openedModelFileName;
        }

        public void RefreshPlugins()
        {
            MessageBox.Show("RefreshPlugins");
        }

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register("SelectedItem", typeof(IPersistEntity), typeof(MainWindow),
                                        new UIPropertyMetadata(null, OnSelectedItemChanged));

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var mw = d as MainWindow;
            if (mw != null && e.NewValue is IPersistEntity)
            {
                var label = (IPersistEntity)e.NewValue;
                //mw.EntityLabel.Text = label != null ? "#" + label.EntityLabel : "";
            }
            else if (mw != null)
            {
                //mw.EntityLabel.Text = "";
            }
        }
        private void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            var corefilters = new[] {
                "Xbim Files|*.xbim;*.xbimf;*.ifc;*.ifcxml;*.ifczip",
                "Ifc File (*.ifc)|*.ifc",
                "xBIM File (*.xBIM)|*.xBIM",
                "IfcXml File (*.IfcXml)|*.ifcxml",
                "IfcZip File (*.IfcZip)|*.ifczip",
                "Zipped File (*.zip)|*.zip"
            };

            var dialog = new OpenFileDialog
            {
                Filter = string.Join("|", corefilters)
            };

            dialog.FileOk += Dialog_FileOk;
            dialog.ShowDialog(this);
        }

        private void Dialog_FileOk(object sender, CancelEventArgs e)
        {
            var dialog = sender as OpenFileDialog;
            if (dialog != null) LoadAnyModel(dialog.FileName);
        }

        private void LoadAnyModel(string fileName)
        {
            var fInfo = new FileInfo(fileName);
            SetWorkerForFileLoad();

            var ext = fInfo.Extension.ToLower();
            switch (ext)
            {
                case ".ifc": //it is an Ifc File
                case ".ifcxml": //it is an IfcXml File
                case ".ifczip": //it is a zip file containing xbim or ifc File
                case ".zip": //it is a zip file containing xbim or ifc File
                case ".xbimf":
                case ".xbim":
                    _loadFileBackgroundWorker.RunWorkerAsync(fileName);
                    break;
                default:
                    MessageBox.Show("Extension '{extension}' has not been recognised. - "+ext);
                    break;
            }
        }

        private void SetWorkerForFileLoad()
        {
            _loadFileBackgroundWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };

            _loadFileBackgroundWorker.ProgressChanged += OnProgressChanged;
            _loadFileBackgroundWorker.DoWork += OpenAcceptableExtension;
            _loadFileBackgroundWorker.RunWorkerCompleted += FileLoadCompleted;
        }

        public XbimDBAccess FileAccessMode { get; set; } = XbimDBAccess.Read;

        private void OpenAcceptableExtension(object sender, DoWorkEventArgs args)
        {
            var worker = sender as BackgroundWorker;
            var seletedFileName = args.Argument as string;

            var model = IfcStore.Open(seletedFileName, null, null, worker.ReportProgress, FileAccessMode);

            //ApplyWorkarounds
            model.AddRevitWorkArounds();
            model.AddWorkAroundTrimForPolylinesIncorrectlySetToOneForEntireCurve();

            if (model.GeometryStore.IsEmpty)
            {
                var context = new Xbim3DModelContext(model);
//#if FastExtrusion
//                context.UseSimplifiedFastExtruder = _simpleFastExtrusion;
//#endif
                SetDeflection(model);

                context.CreateContext(worker.ReportProgress, true);
            }

            args.Result = model;
        }

        private double _deflectionOverride = double.NaN;
        private double _angularDeflectionOverride = double.NaN;
        private void SetDeflection(IfcStore model)
        {
            var mf = model.ModelFactors;
            if (mf == null)
                return;
            if (!double.IsNaN(_angularDeflectionOverride))
                mf.DeflectionAngle = _angularDeflectionOverride;
            if (!double.IsNaN(_deflectionOverride))
                mf.DeflectionTolerance = mf.OneMilliMetre * _deflectionOverride;
        }

        private void FileLoadCompleted(object sender, RunWorkerCompletedEventArgs args)
        {
            if (args.Result is IfcStore) //all ok
            {
                //this Triggers the event to load the model into the views 
                ModelProvider.ObjectInstance = args.Result;
                ModelProvider.Refresh();
                ProgressBar.Value = 0;
                StatusMsg.Text = "Ready";
                //AddRecentFile();
            }
        }

        private void OnProgressChanged(object sender, ProgressChangedEventArgs args)
        {
            if (args.ProgressPercentage < 0 || args.ProgressPercentage > 100)
                return;

            Application.Current.Dispatcher.BeginInvoke(
                DispatcherPriority.Send,
                new Action(() =>
                {
                    ProgressBar.Value = args.ProgressPercentage;
                    StatusMsg.Text = (string)args.UserState;
                }));
        }
    }
}
