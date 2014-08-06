using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Input;
using ContentServerFolderUploader;
using ContentServerUploader.CWS;

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
        }

        private void DoWork(object sender, DoWorkEventArgs e)
        {
            Messages = new ObservableCollection<object>();
            _otAuthentication = new OTAuthentication {AuthenticationToken = AuthenticationToken};
            _counter = 0;
            UploadFolder(ParentId, Path);
        }

        private long _fileProgress;
        public long FileProgress {
            get { return _fileProgress; }
            set
            {
                _fileProgress = value;
                OnPropertyChanged();
            } }
        private void ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            UploaderStatus status = e.UserState as UploaderStatus;
            Counter = e.ProgressPercentage;
            if (status != null)
            {
                if (!string.IsNullOrWhiteSpace(status.Message))
                    Messages.Add(status.Message);
                if(status.IsProgress && status.FileSize>0)
                    FileProgress = (status.BytesRead*100/status.FileSize);
            }
        }

        class UploaderStatus
        {
            public UploaderStatus(string message)
            {
                Message = message;
                IsProgress = false;
            }

            public UploaderStatus(long fileSize,long read)
            {
                FileSize = fileSize;
                BytesRead = read;
                IsProgress = true;
            }
            public long FileSize { get; private set; }
            public long BytesRead { get; private set; }
            public string Message { get; private set; }

            public bool IsProgress { get; private set; }
        }

        public long TotalFiles
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Path))
                    return
                        Directory.EnumerateFileSystemEntries(Path, "*", SearchOption.AllDirectories)
                            .LongCount() + 1;
                return 0;
            }
        }

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

        private void SelectFolder(object param)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.ShowDialog();
            if (string.IsNullOrWhiteSpace(dialog.SelectedPath)) return;
            Path = dialog.SelectedPath;
            _worker = new BackgroundWorker();
            _uploadCommand = new DelegateCommand(_worker.RunWorkerAsync, x => !_worker.IsBusy);
            OnPropertyChanged("UploadCommand");
            _worker.DoWork += DoWork;
            _worker.ProgressChanged += ProgressChanged;
            _worker.WorkerReportsProgress = true;
            _worker.RunWorkerCompleted += RunWorkerCompleted;
        }

        private void RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Messages.Add("Uplod completed.");
        }

        public ICommand SelectFolderCommand
        {
            get { return new DelegateCommand(SelectFolder); }
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
                    _worker.ReportProgress(_counter, new UploaderStatus("Token: " + token));
                    authenticationClient.Close();
                    return token;
                }
                catch (Exception ex)
                {
                    _worker.ReportProgress(_counter, new UploaderStatus(ex.ToString()));
                }
                return null;
            }
        }

        private void UploadFolder(long parentId, string path)
        {
            var id = parentId;
            if (IncludeRootFolder)
                id = CreateFolder(parentId, path);

            var items = Directory.EnumerateFileSystemEntries(path);
            foreach (var item in items)
            {
                var attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
                    UploadFolder(id, item);
                else
                    UploadFile(id, item);
            }
        }

        private long UploadFile(long parentId, string path)
        {
            ContentServiceClient contentService=new ContentServiceClient();            
            DocumentManagementClient docMan = new DocumentManagementClient();
            FileInfo info = new FileInfo(path);
            FileAtts fileAtts = new FileAtts
            {
                CreatedDate = info.CreationTime,
                FileName = info.Name,
                FileSize = info.Length,
                ModifiedDate = info.LastWriteTime
            };            
            /*var attachment = new Attachment
            {
                Contents = File.ReadAllBytes(path),
                CreatedDate = info.CreationTime,
                FileName = info.Name,
                FileSize = info.Length,
                ModifiedDate = info.LastWriteTime
            };*/
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
                _worker.ReportProgress(_counter,new UploaderStatus(isVersionAdded
                        ? string.Format("{{{0}}} - Version {1} added.", id, info.Name)
                        : string.Format("{{{0}}} - Document {1} added.", id, info.Name)));
                
                return 0;
            }
            catch (Exception ex)
            {
                _worker.ReportProgress(_counter, new UploaderStatus(ex.ToString()));
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
            _worker.ReportProgress(_counter,new UploaderStatus(e.Length,e.BytesRead));
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
                _worker.ReportProgress(_counter, new UploaderStatus(string.Format("{{{0}}} - Folder {1} added.", node.ID, info.Name)));
            }
            else
            {
                ++_counter;
                _worker.ReportProgress(_counter, new UploaderStatus(string.Format("{{{0}}} - Folder {1} already exits.",node.ID,info.Name)));
            }            
            return node.ID;
        }
    }
}