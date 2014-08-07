using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Windows.Input;
using ContentServerFolderUploader;
using ContentServerUploader.CWS;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace ContentServerUploader.ViewModels
{
    public class UploaderViewModel : ObservableObject
    {
        private OTAuthentication _otAuthentication;
        private BackgroundWorker _worker;

        public UploaderViewModel()
        {
            _parentId = 2000;
            _includeRootFolder = true;
            _selectFolder = true;

            _worker = new BackgroundWorker();                        
            _uploadCommand = new DelegateCommand(null,x=>false) { Name = "Upload" };
            OnPropertyChanged("UploadCommand");

        }

        private void DoWork(object sender, DoWorkEventArgs e)
        {
            _uploadCommand = new DelegateCommand((x) => { _worker.CancelAsync();},x=>!_worker.CancellationPending) { Name = "Cancel" };
            OnPropertyChanged("UploadCommand");
            _otAuthentication = new OTAuthentication {AuthenticationToken = AuthenticationToken};                        
            FileInfo info = new FileInfo(Path);                        
            if (info.Exists)
                UploadFile(ParentId, Path, e);
            else
                UploadFolder(ParentId, Path, e);
        }

        private long _fileProgress;
        public long FileProgress {
            get { return _fileProgress; }
            set
            {
                _fileProgress = value;
                OnPropertyChanged();
            } }

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

            var hasMessage=propertyInfos.Any(x => x.Name == "Message");
            if(hasMessage) Messages.Add(state.Message);

            var hasCurrentFile = propertyInfos.Any(x => x.Name == "CurrentFile");
            if (hasCurrentFile) Current = state.CurrentFile;

            var hasProgress = propertyInfos.Any(x => x.Name == "Length");
            if (hasProgress &&  state.Length>0) FileProgress = (state.BytesRead * 100 / state.Length);

            
                if (TotalFiles > 0)
                {
                    Progress = (double)(100 * Counter) / TotalFiles;
                }            
        }

        
        public  int TotalFiles { get; private set; }

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
            set { _progress = value;OnPropertyChanged(); }
        }

        private long _parentId;

        public long ParentId
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
            }
        }

        private bool _selectFolder;

        public bool SelectFolder
        {
            get
            {
                return _selectFolder;
            }
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

            if (SelectFolder)
            {
                FolderBrowserDialog folderBrowserDialog= new FolderBrowserDialog();
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
                OpenFileDialog dialog = new OpenFileDialog();                
                dialog.ShowDialog();
                if (string.IsNullOrWhiteSpace(dialog.FileName)) return;
                Path = dialog.FileName;
                if (!string.IsNullOrWhiteSpace(Path))
                    TotalFiles = dialog.FileNames.Length;
                OnPropertyChanged("TotalFiles");
            }
            
            _uploadCommand = new DelegateCommand(x => _worker.RunWorkerAsync(), x => true) { Name = "Upload" };
            OnPropertyChanged("UploadCommand");                        
            Messages = new ObservableCollection<object>();            
            _worker.DoWork += DoWork;
            _worker.ProgressChanged += ProgressChanged;
            _worker.WorkerReportsProgress = true;
            _worker.WorkerSupportsCancellation = true;
            _worker.RunWorkerCompleted += RunWorkerCompleted;            
        }

        private void RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Messages.Add(e.Cancelled ? "Upload Cancelled." : "Completed.");
            _worker.DoWork -= DoWork;
            _worker.ProgressChanged -= ProgressChanged;            
            _worker.RunWorkerCompleted -= RunWorkerCompleted;
            _uploadCommand = new DelegateCommand(null, x => false) { Name = "Upload" };
            OnPropertyChanged("UploadCommand");            

        }

        public ICommand SelectCommand
        {
            get { return new DelegateCommand(Select); }
        }

        private ICommand _uploadCommand;

        public ICommand UploadCommand
        {
            get { return _uploadCommand; }
        }

        private string AuthenticationToken
        {
            get
            {
                try
                {
                    AuthenticationClient authenticationClient = new AuthenticationClient();
                    var token = authenticationClient.AuthenticateUser(Config.UserName,
                        Config.Password);
                    _worker.ReportProgress(_counter, new{Message="Token: " + token});
                    authenticationClient.Close();
                    return token;
                }
                catch (Exception ex)
                {
                    _worker.ReportProgress(_counter, new{Message=ex.ToString()});
                }
                return null;
            }
        }
        

        private void UploadFolder(long parentId, string path,DoWorkEventArgs e)
        {
            if (_worker.CancellationPending) {e.Cancel = true;
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
                    UploadFolder(id, item,e);
                else
                    UploadFile(id, item,e);
            }
        }

        private long UploadFile(long parentId, string path,DoWorkEventArgs e)
        {
            if (_worker.CancellationPending){ e.Cancel = true;
                return -1;
            }             
            ContentServiceClient contentService=new ContentServiceClient();            
            DocumentManagementClient docMan = new DocumentManagementClient();
            FileInfo info = new FileInfo(path);
            _worker.ReportProgress(_counter, new {CurrentFile=info.Name});
            FileAtts fileAtts = new FileAtts
            {
                CreatedDate = info.CreationTime,
                FileName = info.Name,
                FileSize = info.Length,
                ModifiedDate = info.LastWriteTime
            };                        
            var stream = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            ProgressStream pStream = new ProgressStream(stream);
            pStream.ProgressChanged += pStream_ProgressChanged;
            try
            {
                bool isVersionAdded = false;
                var res =
                    docMan.GetNodeByName(ref _otAuthentication, parentId,
                        info.Name);                
                string contextId;
                if (res == null)
                {
                    contextId=docMan.CreateDocumentContext(ref _otAuthentication, parentId, info.Name, null,
                        false, null);                                        
                }
                else
                {                    
                    contextId = docMan.AddVersionContext(ref _otAuthentication, res.ID, null);
                    isVersionAdded = true;
                }
                var id = contentService.UploadContent(ref _otAuthentication, contextId, fileAtts, pStream);
                ++_counter;
                _worker.ReportProgress(_counter,new {Message=isVersionAdded
                        ? string.Format("{{{0}}} - Version {1} added.", id, info.Name)
                        : string.Format("{{{0}}} - Document {1} added.", id, info.Name)});                               
            }
            catch (Exception ex)
            {
                _worker.ReportProgress(_counter, new {Message=ex.ToString()});
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
            _worker.ReportProgress(_counter,new {e.Length,e.BytesRead});
        }

        private ObservableCollection<object> _messages;

        public ObservableCollection<object> Messages
        {
            get { return _messages; }
            set
            {
                _messages = value;
                OnPropertyChanged();
            }
        }

        private long CreateFolder(long parentId, string path)
        {
            DirectoryInfo info = new DirectoryInfo(path);
            _worker.ReportProgress(_counter, new {CurrentFile=info.Name});
            DocumentManagementClient documentManagementClient = new DocumentManagementClient();
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
                _worker.ReportProgress(_counter, new{Message=string.Format("{{{0}}} - Folder {1} added.", node.ID, info.Name)});
            }
            else
            {
                ++_counter;
                _worker.ReportProgress(_counter, new{Message=string.Format("{{{0}}} - Folder {1} already exits.",node.ID,info.Name)});
            }            
            return node.ID;
        }
    }
}