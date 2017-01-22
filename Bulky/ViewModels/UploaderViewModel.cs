using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Windows.Input;
using Bulky.CWS;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Bulky.ViewModels
{
    public class UploaderViewModel : ObservableObject
    {
        private OTAuthentication _otAuthentication;
        private readonly BackgroundWorker _worker;

        private int _folders;
        private int _files;
        private int _versions;
        private int _failed;
        private int _folderReused;

        public UploaderViewModel()
        {
            _parentId = 2000;
            _includeRootFolder = true;
            _selectFolder = true;
            _autoScrollLogs = false;

            _worker = new BackgroundWorker();
            _uploadCommand = new DelegateCommand(null, x => false) {Name = "Upload"};
            OnPropertyChanged("UploadCommand");
            OnPropertyChanged("CopyCommand");
        }

        private void DoWork(object sender, DoWorkEventArgs e)
        {
            _uploadCommand = new DelegateCommand((x) => { _worker.CancelAsync(); }, x => !_worker.CancellationPending)
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

        private long _fileProgress;

        public long FileProgress
        {
            get { return _fileProgress; }
            set
            {
                _fileProgress = value;
                OnPropertyChanged();
            }
        }

        private string _current;

        public string Current
        {
            get { return _current; }
            set
            {
                _current = value;
                OnPropertyChanged();
            }
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
            if (hasProgress && state.Length > 0) FileProgress = (state.BytesRead * 100 / state.Length);


            if (TotalFiles > 0)
            {
                Progress = (double) (100 * Counter) / TotalFiles;
            }
        }


        public int TotalFiles { get; private set; }

        private int _counter;

        public int Counter
        {
            get { return _counter; }
            set
            {
                _counter = value;
                OnPropertyChanged();
            }
        }

        private double _progress;

        public double Progress
        {
            get { return _progress; }
            set
            {
                _progress = value;
                OnPropertyChanged();
            }
        }

        private int _parentId;

        public int ParentId
        {
            get { return _parentId; }
            set
            {
                _parentId = value;
                OnPropertyChanged();
            }
        }

        private string _path;

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

        private bool _includeRootFolder;

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
                    {
                        TotalFiles += 1;
                    }
                    else
                    {
                        TotalFiles -= 1;
                    }
                    OnPropertyChanged("TotalFiles");
                }
            }
        }

        private bool _autoScrollLogs;
        public bool AutoScrollLogs {
            get { return _autoScrollLogs; }
            set
            {
                _autoScrollLogs = value;
                OnPropertyChanged();
            }
        }

        private bool _selectFolder;

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

        private void Select(object param)
        {
            Counter = 0;
            Current = null;
            FileProgress = 0;
            Progress = 0;
            TotalFiles = 0;

            _folders = 0;
            _files = 0;
            _versions = 0;
            _failed = 0;
            _folderReused = 0;

            if (SelectFolder)
            {
                var folderBrowserDialog = new FolderBrowserDialog()
                {
                    Description = "Select the folder that you want to upload to Content Server. All the subfolders and files from the selected folder will be uploaded to Content Server."
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
                OnPropertyChanged("TotalFiles");
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

            _uploadCommand = new DelegateCommand(x => _worker.RunWorkerAsync(), x => true) {Name = "Upload"};
            OnPropertyChanged("UploadCommand");
            Messages = new ObservableCollection<LogMessage>();
            _worker.DoWork += DoWork;
            _worker.ProgressChanged += ProgressChanged;
            _worker.WorkerReportsProgress = true;
            _worker.WorkerSupportsCancellation = true;
            _worker.RunWorkerCompleted += RunWorkerCompleted;
        }

        private void RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Messages.Add(new LogMessage(e.Cancelled ? "Upload Cancelled." : "Completed."));
            Current = null;
            FileProgress = 0;
            Messages.Add(new LogMessage($"Summary:\n\tFolders created: {_folders} ({_folderReused})\n\tFiles created: {_files}\n\tVersions created: {_versions}\n\tFailed to upload: {_failed}", Severity.Warn));
            _worker.DoWork -= DoWork;
            _worker.ProgressChanged -= ProgressChanged;
            _worker.RunWorkerCompleted -= RunWorkerCompleted;
            _uploadCommand = new DelegateCommand(null, x => false) {Name = "Upload"};
            OnPropertyChanged("UploadCommand");
        }

        public ICommand SelectCommand => new DelegateCommand(Select);

        private ICommand _uploadCommand;

        public ICommand UploadCommand => _uploadCommand;

        private void Copy(object param)
        {
            if(param != null)
                Clipboard.SetText((string)param);
        }
        public ICommand CopyCommand => new DelegateCommand(Copy);

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
                    _worker.ReportProgress(_counter, new LogMessage(ex.ToString(),Severity.Error));
                }
                return null;
            }
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
                _worker.ReportProgress(_counter, new LogMessage($"Error in uploading {info.Name}\n{ex.Message}",Severity.Error));
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

        void pStream_ProgressChanged(object sender, ProgressStream.ProgressChangedEventArgs e)
        {
            _worker.ReportProgress(_counter, new {e.Length, e.BytesRead});
        }

        private ObservableCollection<LogMessage> _messages;

        public ObservableCollection<LogMessage> Messages
        {
            get { return _messages; }
            set
            {
                _messages = value;
                OnPropertyChanged();
            }
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