using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Windows.Input;
using Bulky.CWS;
using Bulky.Properties;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Bulky.ViewModels
{
    public class UploaderViewModel : ObservableObject
    {
        private bool _autoScrollLogs;

        private int _counter;

        private string _current;
        private int _failed;

        private long _fileProgress;
        private int _files;
        private int _folderReused;

        private int _folders;
        private bool _cancelledNotied = false;
        private bool _includeRootFolder;

        private ObservableCollection<LogMessage> _messages;
        private OTAuthentication _otAuthentication;

        private int _parentId;

        private string _path;

        private double _progress;

        private bool _selectFolder;

        private int _versions;
        private BackgroundWorker _worker;

        public UploaderViewModel()
        {
            _parentId = 2000;
            _includeRootFolder = true;
            _selectFolder = true;
            _autoScrollLogs = false;

            UploadCommand = new DelegateCommand(null, x => false) {Name = "Upload"};
            OnPropertyChanged("UploadCommand");

            SelectCommand = new DelegateCommand(Select);

            OnPropertyChanged("CopyCommand");
        }

        public long FileProgress
        {
            get { return _fileProgress; }
            set
            {
                _fileProgress = value;
                OnPropertyChanged();
            }
        }

        public string Current
        {
            get { return _current; }
            set
            {
                _current = value;
                OnPropertyChanged();
            }
        }


        public int TotalFiles { get; private set; }

        public int Counter
        {
            get { return _counter; }
            set
            {
                _counter = value;
                OnPropertyChanged();
            }
        }

        public double Progress
        {
            get { return _progress; }
            set
            {
                _progress = value;
                OnPropertyChanged();
            }
        }

        public int ParentId
        {
            get { return _parentId; }
            set
            {
                _parentId = value;
                OnPropertyChanged();
            }
        }

        public string Path
        {
            get { return _path; }
            set
            {
                _path = value;
                OnPropertyChanged();
                OnPropertyChanged("TotalFiles");
            }
        }

        public bool IncludeRootFolder
        {
            get { return _includeRootFolder; }
            set
            {
                _includeRootFolder = value;
                OnPropertyChanged();
                if (TotalFiles > 0)
                {
                    if (_includeRootFolder)
                        TotalFiles += 1;
                    else
                        TotalFiles -= 1;
                    OnPropertyChanged("TotalFiles");
                }
            }
        }

        public bool AutoScrollLogs
        {
            get { return _autoScrollLogs; }
            set
            {
                _autoScrollLogs = value;
                OnPropertyChanged();
            }
        }

        public bool SelectFolder
        {
            get { return _selectFolder; }
            set
            {
                _selectFolder = value;
                if (!_selectFolder)
                    IncludeRootFolder = false;
                OnPropertyChanged();
            }
        }

        public ICommand SelectCommand { get; private set; }

        public ICommand UploadCommand { get; private set; }

        public ICommand CopyCommand => new DelegateCommand(param =>
        {
            if (param != null)
                Clipboard.SetText((string) param);
        });

        private string AuthenticationToken
        {
            get
            {
                try
                {
                    var authenticationClient = new AuthenticationClient();
                    var token = authenticationClient.AuthenticateUser(Config.UserName,
                        Config.Password);
                    _worker.ReportProgress(_counter, new LogMessage("Token: " + token));
                    authenticationClient.Close();
                    return token;
                }
                catch (Exception ex)
                {
                    _worker.ReportProgress(_counter, new LogMessage(ex.ToString(), Severity.Error));
                }
                return null;
            }
        }

        public ObservableCollection<LogMessage> Messages
        {
            get { return _messages; }
            set
            {
                _messages = value;
                OnPropertyChanged();
            }
        }

        private void DoWork(object sender, DoWorkEventArgs e)
        {
            UploadCommand = new DelegateCommand(x => { _worker.CancelAsync(); }, x => !_worker.CancellationPending)
            {
                Name = "Cancel"
            };
            OnPropertyChanged("UploadCommand");
            _otAuthentication = new OTAuthentication {AuthenticationToken = AuthenticationToken};
            var info = new FileInfo(Path);
            if (info.Exists)
                UploadFile(ParentId, Path, e);
            else
                UploadFolder(ParentId, Path, e);
        }

        private void ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            Counter = e.ProgressPercentage;

            dynamic state = e.UserState;

            if (state == null) return;
            PropertyInfo[] propertyInfos = state.GetType().GetProperties();

            var hasMessage = propertyInfos.Any(x => x.Name == "Message");
            if (hasMessage) Messages.Add(state as LogMessage);

            var hasCurrentFile = propertyInfos.Any(x => x.Name == "CurrentFile");
            if (hasCurrentFile) Current = state.CurrentFile;

            var hasProgress = propertyInfos.Any(x => x.Name == "Length");
            if (hasProgress && state.Length > 0)
            {
                if (_worker.CancellationPending && !_cancelledNotied)
                {
                    Messages.Add(new LogMessage($"Process will be cancelled, once the current file ({Current}) upload completes.",Severity.Warn));
                    _cancelledNotied = true;
                }
                FileProgress = state.BytesRead * 100 / state.Length;
            }


            if (TotalFiles > 0)
                Progress = (double) (100 * Counter) / TotalFiles;
        }

        private void Select(object param)
        {
            Counter = 0;
            Current = null;
            FileProgress = 0;
            Progress = 0;
            TotalFiles = 0;
            Messages = new ObservableCollection<LogMessage>();

            _folders = 0;
            _files = 0;
            _versions = 0;
            _failed = 0;
            _folderReused = 0;


            //Setup background worker
            _worker = new BackgroundWorker();
            _worker.DoWork += DoWork;
            _worker.ProgressChanged += ProgressChanged;
            _worker.WorkerReportsProgress = true;
            _worker.WorkerSupportsCancellation = true;
            _worker.RunWorkerCompleted += RunWorkerCompleted;

            if (SelectFolder)
            {
                var folderBrowserDialog = new FolderBrowserDialog
                {
                    Description = Resources.FolderBrowserDescription
                };
                folderBrowserDialog.ShowDialog();
                if (string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath)) return;
                Path = folderBrowserDialog.SelectedPath;
                if (!string.IsNullOrWhiteSpace(Path))
                    TotalFiles = (int)
                        Directory.EnumerateFileSystemEntries(Path, "*", SearchOption.AllDirectories)
                            .LongCount();
                if (IncludeRootFolder)
                    TotalFiles += 1;
                OnPropertyChanged(@"TotalFiles");
            }
            else
            {
                var dialog = new OpenFileDialog();
                dialog.ShowDialog();
                if (string.IsNullOrWhiteSpace(dialog.FileName)) return;
                Path = dialog.FileName;
                if (!string.IsNullOrWhiteSpace(Path))
                    TotalFiles = dialog.FileNames.Length;
                OnPropertyChanged("TotalFiles");
            }


            UploadCommand = new DelegateCommand(a =>
            {
                _worker.RunWorkerAsync();
                SelectCommand = new DelegateCommand(null, x => false) {Name = "Select"};
                OnPropertyChanged("SelectCommand");
            }, x => true) {Name = "Upload"};
            OnPropertyChanged("UploadCommand");
        }

        private void RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Current = null;
            FileProgress = 0;

            Messages.Add(new LogMessage(e.Cancelled ? "Upload Cancelled." : "Completed."));
            Messages.Add(
                new LogMessage(
                    $"Summary:\n\tFolders created: {_folders} ({_folderReused})\n\tFiles created: {_files}\n\tVersions created: {_versions}\n\tFailed to upload: {_failed}",
                    Severity.Warn));

            //Unregister backroundworker events
            _worker.DoWork -= DoWork;
            _worker.ProgressChanged -= ProgressChanged;
            _worker.RunWorkerCompleted -= RunWorkerCompleted;

            SelectCommand = new DelegateCommand(Select);
            OnPropertyChanged("SelectCommand");

            UploadCommand = new DelegateCommand(null, x => false) {Name = "Upload"};
            OnPropertyChanged("UploadCommand");
        }


        private void UploadFolder(int parentId, string path, DoWorkEventArgs e)
        {
            if (_worker.CancellationPending)
            {
                e.Cancel = true;
                return;
            }
            var id = parentId;
            if (IncludeRootFolder)
                id = CreateFolder(parentId, path);

            var items = Directory.EnumerateFileSystemEntries(path);
            foreach (var item in items)
            {
                var attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
                    UploadFolder(id, item, e);
                else
                    UploadFile(id, item, e);
            }
        }

        private long UploadFile(int parentId, string path, DoWorkEventArgs e)
        {
            if (_worker.CancellationPending)
            {
                e.Cancel = true;
                return -1;
            }
            var contentService = new ContentServiceClient();
            var docMan = new DocumentManagementClient();
            var info = new FileInfo(path);
            _worker.ReportProgress(_counter, new {CurrentFile = info.Name});
            var fileAtts = new FileAtts
            {
                CreatedDate = info.CreationTime,
                FileName = info.Name,
                FileSize = info.Length,
                ModifiedDate = info.LastWriteTime
            };
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var pStream = new ProgressStream(stream);
            pStream.ProgressChanged += pStream_ProgressChanged;
            try
            {
                var isVersionAdded = false;
                var res =
                    docMan.GetNodeByName(ref _otAuthentication, parentId,
                        info.Name);
                string contextId;
                if (res == null)
                {
                    contextId = docMan.CreateDocumentContext(ref _otAuthentication, parentId, info.Name, null,
                        false, null);
                }
                else
                {
                    contextId = docMan.AddVersionContext(ref _otAuthentication, res.ID, null);
                    isVersionAdded = true;
                }
                var id = contentService.UploadContent(ref _otAuthentication, contextId, fileAtts, pStream);
                ++_counter;
                if (isVersionAdded)
                    _versions++;
                else
                    _files++;
                _worker.ReportProgress(_counter, new LogMessage(isVersionAdded
                    ? $"{{{id}}} - Version {info.Name} added."
                    : $"{{{id}}} - Document {info.Name} added."
                ));
            }
            catch (Exception ex)
            {
                ++_counter;
                _worker.ReportProgress(_counter,
                    new LogMessage($"Error in uploading {info.Name}\n{ex.Message}", Severity.Error));
                _failed++;
            }
            finally
            {
                contentService.Close();
                docMan.Close();
                stream.Close();
            }
            return -1;
        }

        private void pStream_ProgressChanged(object sender, ProgressStream.ProgressChangedEventArgs e)
        {
            _worker.ReportProgress(_counter, new {e.Length, e.BytesRead});
        }

        private int CreateFolder(int parentId, string path)
        {
            var info = new DirectoryInfo(path);
            _worker.ReportProgress(_counter, new {CurrentFile = info.Name});
            var documentManagementClient = new DocumentManagementClient();
            var node = documentManagementClient.GetNodeByName(ref _otAuthentication, parentId,
                info.Name);
            if (node == null)
            {
                var createFolderRes = documentManagementClient.CreateFolder(ref _otAuthentication,
                    parentId,
                    info.Name, null, null);
                documentManagementClient.Close();
                node = createFolderRes;
                ++_counter;
                _worker.ReportProgress(_counter,
                    new LogMessage($"{{{node.ID}}} - Folder {info.Name} added."));
                _folders++;
            }
            else
            {
                ++_counter;
                _folderReused++;
                _worker.ReportProgress(_counter,
                    new LogMessage($"{{{node.ID}}} - Folder {info.Name} already exits."));
            }
            return node.ID;
        }
    }
}