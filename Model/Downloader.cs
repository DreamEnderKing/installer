using COSXML.CosException;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vision;

namespace installer.Model
{
    public struct UpdateInfo                                         // 更新信息，包括新版本版本号、更改文件数和新文件数
    {
        public string status;
        public int changedFileCount;
        public int newFileCount;
    }

    public class Downloader
    {
        #region 属性区
        public class UserInfo
        {
            public string _id = "";
            public string email = "";
        }
        public string ProgramName = "THUAI6";                     // 要运行或下载的程序名称
        public string StartName = "maintest.exe";          // 启动的程序名
        private Local_Data Data;
        private Tencent_Cos Cloud;

        private HttpClient Client = new HttpClient();
        private EEsast Web = new EEsast();

        public enum UpdateStatus
        {
            success, unarchieving, downloading, hash_computing, error
        } //{ newUser, menu, move, working, initializing, disconnected, error, successful, login, web, launch };
        public UpdateStatus Status;

        ConcurrentQueue<string> downloadFile = new ConcurrentQueue<string>(); // 需要下载的文件名
        ConcurrentQueue<string> downloadFailed = new ConcurrentQueue<string>();  //更新失败的文件名
        public List<string> UpdateFailed
        {
            get { return downloadFailed.ToList(); }
        }
        public bool UpdatePlanned
        {
            get; set;
        }

        public void ResetDownloadFailedInfo()
        {
            downloadFailed.Clear();
        }

        private int filenum = 0;                                   // 总文件个数

        public string Route { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string UserId { get => Web.ID; }
        public string UserEmail { get => Web.Email; }
        public string CodeRoute { get; set; } = string.Empty;
        public string? Language { get; set; } = null;
        public string PlayerNum { get; set; } = "nSelect";
        public enum LaunchLanguage { cpp, python };
        public LaunchLanguage launchLanguage { get; set; } = LaunchLanguage.cpp;
        public enum UsingOS { Win, Linux, OSX };
        public UsingOS usingOS { get; set; }
        public class Updater
        {
            public string Message;
            public bool Working { get; set; }
            public bool CombatCompleted { get => false; }
            public bool UploadReady { get; set; } = false;
            public bool ProfileAvailable { get; set; }
        }
        public bool LoginFailed { get; set; } = false;
        public bool RememberMe { get; set; }

        #endregion

        #region 方法区
        public Downloader()
        {
            Data = new Local_Data();
            Route = Data.InstallPath;
            Cloud = new Tencent_Cos("1314234950", "ap-beijing", "thuai6");
            usingOS = ReadUsingOS();
        }

        public void UpdateMD5()
        {
            if (File.Exists(Data.MD5DataPath))
                File.Delete(Data.MD5DataPath);
            Status = UpdateStatus.downloading;
            Cloud.DownloadFileAsync(Data.MD5DataPath, "hash.json").Wait();
            if (Cloud.Exceptions.Count > 0)
            {
                Status = UpdateStatus.error;
                return;
            }
            Data.ReadMD5Data();
        }

        /// <summary>
        /// 全新安装
        /// </summary>
        public void Install()
        {
            UpdateMD5();
            if (Status == UpdateStatus.error) return;

            if (Directory.Exists(Data.InstallPath))
                Directory.Delete(Data.InstallPath, true);

            Data.Installed = false;
            string zp = Path.Combine(Data.InstallPath, "THUAI7.tar.gz");
            Status = UpdateStatus.downloading;
            Cloud.DownloadFileAsync(zp, "THUAI7.tar.gz").Wait();
            Status = UpdateStatus.unarchieving;
            Cloud.ArchieveUnzip(zp, Data.InstallPath);
            File.Delete(zp);

            Data.ResetInstallPath(Data.InstallPath);
            Status = UpdateStatus.hash_computing;
            Data.ScanDir();
            if (Data.MD5Update.Count != 0)
            {
                // TO DO: 下载文件与hash校验值不匹配修复
                Status = UpdateStatus.error;
                Update();
            }
            else
            {
                Status = UpdateStatus.success;
            }
        }

        /// <summary>
        /// 检测是否需要进行更新
        /// 返回真时则表明需要更新
        /// </summary>
        /// <returns></returns>
        public bool CheckUpdate()
        {
            UpdateMD5();
            Data.MD5Update.Clear();
            Status = UpdateStatus.hash_computing;
            Data.ScanDir();
            return Data.MD5Update.Count != 0;
        }

        /// <summary>
        /// 更新文件
        /// </summary>
        public void Update()
        {
            if (CheckUpdate())
            {
                Status = UpdateStatus.downloading;
                Cloud.DownloadQueueAsync(new ConcurrentQueue<string>(Data.MD5Update), downloadFailed).Wait();
                if (downloadFailed.Count == 0)
                {
                    Data.MD5Update.Clear();
                    Status = UpdateStatus.hash_computing;
                    Data.ScanDir();
                    if (Data.MD5Update.Count == 0)
                    {
                        Status = UpdateStatus.success;
                        return;
                    }
                }
            }
            else
            {
                Status = UpdateStatus.success;
                return;
            }
            Status = UpdateStatus.error;
        }

        public async Task Login()
        {
            await Web.LoginToEEsast(Client, Username, Password);
            Data.Config["Token"] = Web.Token;
            Data.SaveConfig();
        }

        public bool RememberUser()
        {
            int result = 0;
            result |= Web.WriteJson("email", Username);
            result |= Web.WriteJson("password", Password);
            return result == 0;
        }
        public bool RecallUser()
        {
            var username = Web.ReadJson("email");
            if (username == null || username.Equals(""))
            {
                Username = "";
                return false;
            }
            Username = username;

            var password = Web.ReadJson("password");
            if (password == null || password.Equals(""))
            {
                Password = "";
                return false;
            }
            Password = password;

            return true;
        }
        public bool ForgetUser()
        {
            int result = 0;
            result |= Web.WriteJson("email", "");
            result |= Web.WriteJson("password", "");
            return result == 0;
        }

        public bool Update()
        {
            try
            {
                return Cloud.Update();
            }
            catch
            {
                return false;
            }
        }
        public int Uninst()
        {
            return Cloud.DeleteAll();
        }

        public bool Launch()
        {
            if (Cloud.CheckAlreadyDownload())
            {
                //Process.Start(System.IO.Path.Combine(Data.InstallPath, startName));
                switch (RunProgram.RunInfo.mode)
                {
                    case RunProgram.RunMode.ServerOnly:
                        RunProgram.StartServer(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                            RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        break;
                    case RunProgram.RunMode.ServerForDebugOnly:
                        RunProgram.StartServerForDebug(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                            RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        break;
                    case RunProgram.RunMode.GUIAttendGameOnly:
                        RunProgram.RunGUIClient(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.characterID,
                            false, RunProgram.RunInfo.occupation, RunProgram.RunInfo.type);
                        break;
                    case RunProgram.RunMode.GUIVisit:
                        RunProgram.RunGUIClient(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, 0, true, 1, 1);
                        break;
                    case RunProgram.RunMode.GUIAndAICpp:
                        RunProgram.StartServerForDebug(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        Task.Delay(100);
                        RunProgram.RunCpp(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.saveDebugLog, RunProgram.RunInfo.showDebugLog,
                            RunProgram.RunInfo.warningOnly, RunProgram.RunInfo.playerId, RunProgram.RunInfo.filePath);
                        RunProgram.RunInfo.playerId = null;
                        RunProgram.RunInfo.filePath = null;
                        RunProgram.RunGUIClient(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.characterID,
                        false, RunProgram.RunInfo.occupation, RunProgram.RunInfo.type);
                        break;
                    case RunProgram.RunMode.GUIAndAIPython:
                        RunProgram.StartServerForDebug(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        Task.Delay(100);
                        RunProgram.RunPython(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.saveDebugLog, RunProgram.RunInfo.showDebugLog,
                            RunProgram.RunInfo.warningOnly, RunProgram.RunInfo.playerId, RunProgram.RunInfo.filePath);
                        RunProgram.RunInfo.playerId = null;
                        RunProgram.RunInfo.filePath = null;
                        RunProgram.RunGUIClient(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.characterID,
                        false, RunProgram.RunInfo.occupation, RunProgram.RunInfo.type);
                        break;
                    case RunProgram.RunMode.ServerAndCpp:
                        RunProgram.StartServer(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        Task.Delay(100);
                        RunProgram.RunCpp(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.saveDebugLog, RunProgram.RunInfo.showDebugLog,
                            RunProgram.RunInfo.warningOnly, RunProgram.RunInfo.playerId, RunProgram.RunInfo.filePath);
                        RunProgram.RunInfo.playerId = null;
                        RunProgram.RunInfo.filePath = null;
                        break;
                    case RunProgram.RunMode.ServerAndPython:
                        RunProgram.StartServer(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        Task.Delay(100);
                        RunProgram.RunPython(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.saveDebugLog, RunProgram.RunInfo.showDebugLog,
                            RunProgram.RunInfo.warningOnly, RunProgram.RunInfo.playerId, RunProgram.RunInfo.filePath);
                        RunProgram.RunInfo.playerId = null;
                        RunProgram.RunInfo.filePath = null;
                        break;
                    case RunProgram.RunMode.ServerAndCppVisit:
                        RunProgram.StartServer(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        Task.Delay(100);
                        RunProgram.RunCpp(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.saveDebugLog, RunProgram.RunInfo.showDebugLog,
                            RunProgram.RunInfo.warningOnly, RunProgram.RunInfo.playerId, RunProgram.RunInfo.filePath);
                        RunProgram.RunInfo.playerId = null;
                        RunProgram.RunInfo.filePath = null;
                        RunProgram.RunGUIClient(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.characterID, true, 0, 1);
                        break;
                    case RunProgram.RunMode.ServerAndPythonVisit:
                        RunProgram.StartServer(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        Task.Delay(100);
                        RunProgram.RunPython(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.saveDebugLog, RunProgram.RunInfo.showDebugLog,
                            RunProgram.RunInfo.warningOnly, RunProgram.RunInfo.playerId, RunProgram.RunInfo.filePath);
                        RunProgram.RunInfo.playerId = null;
                        RunProgram.RunInfo.filePath = null;
                        RunProgram.RunGUIClient(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.characterID, true, 0, 1);
                        break;
                    case RunProgram.RunMode.ServerDebugAndCppVisit:
                        RunProgram.StartServerForDebug(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        Task.Delay(100);
                        RunProgram.RunCpp(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.saveDebugLog, RunProgram.RunInfo.showDebugLog,
                            RunProgram.RunInfo.warningOnly, RunProgram.RunInfo.playerId, RunProgram.RunInfo.filePath);
                        RunProgram.RunInfo.playerId = null;
                        RunProgram.RunInfo.filePath = null;
                        RunProgram.RunGUIClient(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.characterID, true, 0, 1);
                        break;
                    case RunProgram.RunMode.ServerDebugAndPythonVisit:
                        RunProgram.StartServerForDebug(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.gameTimeSec, RunProgram.RunInfo.playbackFileName);
                        Task.Delay(100);
                        RunProgram.RunPython(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.studentCount,
                        RunProgram.RunInfo.trickerCount, RunProgram.RunInfo.saveDebugLog, RunProgram.RunInfo.showDebugLog,
                            RunProgram.RunInfo.warningOnly, RunProgram.RunInfo.playerId, RunProgram.RunInfo.filePath);
                        RunProgram.RunInfo.playerId = null;
                        RunProgram.RunInfo.filePath = null;
                        RunProgram.RunGUIClient(RunProgram.RunInfo.IP, RunProgram.RunInfo.port, RunProgram.RunInfo.characterID, true, 0, 1);
                        break;
                }
                return true;
            }
            else
            {
                //MessageBox.Show($"文件还不存在，请安装主体文件", "文件不存在", //MessageBoxButton.OK, //MessageBoxImage.Warning, //MessageBoxResult.OK);
                return false;
            }
        }

        public async Task<int> Upload()
        {
            switch (CodeRoute.Substring(CodeRoute.LastIndexOf('.') + 1))
            {
                case "cpp":
                case "h":
                    Language = "cpp";
                    break;
                case "py":
                    Language = "python";
                    break;
                default:
                    return -8;
            }
            if (PlayerNum.Equals("nSelect"))
                return -9;
            return await web.UploadFiles(client, CodeRoute, Language, PlayerNum);
        }
        public bool WriteUsingOS()
        {
            string OS = "";
            switch (usingOS)
            {
                case UsingOS.Win:
                    OS = "win";
                    break;
                case UsingOS.Linux:
                    OS = "linux";
                    break;
                case UsingOS.OSX:
                    OS = "osx";
                    break;
            }
            return Web.WriteJson("OS", OS) == 0;
        }
        public UsingOS ReadUsingOS()
        {
            return Web.ReadJson("OS") switch
            {
                "linux" => UsingOS.Linux,
                "osx" => UsingOS.OSX,
                _ => UsingOS.Win,
            };
        }

        public void GetNewHash()
        {
            Cloud.Download(System.IO.Path.Combine(Data.InstallPath, "hash.json"), "hash.json");
        }
        #endregion
    }


}
