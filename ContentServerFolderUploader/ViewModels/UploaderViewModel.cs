using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Input;
using ContentServerFolderUploader.CWS;

namespace ContentServerFolderUploader.ViewModels
{
    public class UploaderViewModel : ObservableObject
    {
        private OTAuthentication _otAuthentication;
        private BackgroundWorker _worker;

        public UploaderViewModel()
        {
            _parentId = 2000;            
        }

        void DoWork(object sender, DoWorkEventArgs e)
        {
            Messages = new ObservableCollection<object>();
            _otAuthentication = new OTAuthentication { AuthenticationToken = AuthenticationToken };
            _counter = 0;                        
            UploadFolder(ParentId, Path);
        }

        void ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            Counter = e.ProgressPercentage;
            Messages.Add(e.UserState);
        }
        
        public long TotalFiles
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Path))
                    return Directory.EnumerateFileSystemEntries(Path,"*",SearchOption.AllDirectories).LongCount()+1;
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

        private void SelectFolder(object param)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.ShowDialog();
            if (dialog.SelectedPath != null)
            {
                Path = dialog.SelectedPath;
                _worker = new BackgroundWorker();
                _uploadCommand = new DelegateCommand(_worker.RunWorkerAsync, x => !_worker.IsBusy);
                OnPropertyChanged("UploadCommand");
                _worker.DoWork += DoWork;
                _worker.ProgressChanged += ProgressChanged;
                _worker.WorkerReportsProgress = true;
                _worker.RunWorkerCompleted += RunWorkerCompleted;
            }
        }

        void RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Messages.Add("Uploda completed.");
        }

        public ICommand SelectFolderCommand
        {
            get { return new DelegateCommand(SelectFolder);}
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
                AuthenticationClient authenticationClient = new AuthenticationClient();
                var token = authenticationClient.AuthenticateUser("Admin", "livelink");
                _worker.ReportProgress(_counter, "Token: " + token);                
                authenticationClient.Close();                
                return token;
            }
        }
        void UploadFolder(long parentId, string path)
        {
            var id = CreateFolder(parentId, path);
            var items = Directory.EnumerateFileSystemEntries(path);
            foreach (var item in items)
            {
                var attributes = File.GetAttributes(item);
                if((attributes & FileAttributes.Directory) == FileAttributes.Directory)
                    UploadFolder(id,item);
                else                
                    UploadFile(id, item);                
            }            
        }        

        long UploadFile(long parentId, string path)
        {
            DocumentManagementClient documentManagementClient = new DocumentManagementClient();
            Node node = new Node();
            FileInfo info = new FileInfo(path);
            var attachment = new Attachment
            {
                Contents = File.ReadAllBytes(path),
                CreatedDate = info.CreationTime,
                FileName = info.Name,
                FileSize = info.Length,
                ModifiedDate = info.LastWriteTime
            };
            try
            {
                var res =                    
                        documentManagementClient.GetNodeByName(ref _otAuthentication, parentId,
                            info.Name);
                node = res;
                if (res == null)
                {
                    var respose =                        
                            documentManagementClient.CreateDocument(ref _otAuthentication, parentId,
                                info.Name, null,
                                false, null,attachment );
                    node = respose;
                    ++_counter;
                    _worker.ReportProgress(_counter, "Document " + info.Name + " added.");                    
                }
                else
                {                    
                            documentManagementClient.AddVersion(ref _otAuthentication,
                                res.ID, null, attachment);
                            ++_counter;
                            _worker.ReportProgress(_counter, "Version " + info.Name + " added.");
                }
                return node.ID;
            }
            catch (Exception ex)
            {                
                _worker.ReportProgress(_counter,ex.ToString());              
            }
            finally
            {
                documentManagementClient.Close();
            }
            return -1;
        }

        private ObservableCollection<object> _messages;
        public ObservableCollection<object> Messages {
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
            var node = documentManagementClient.GetNodeByName(ref _otAuthentication, parentId, info.Name);            
            if (node == null)
            {
                var createFolderRes = documentManagementClient.CreateFolder(ref _otAuthentication, parentId,
                    info.Name, null, null);
                documentManagementClient.Close();                
                node = createFolderRes;
            }            
            ++_counter;
            _worker.ReportProgress(_counter, "Folder " + info.Name + " added.");
            return node.ID;
        }

    }
}