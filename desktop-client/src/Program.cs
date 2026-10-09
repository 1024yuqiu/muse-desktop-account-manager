using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MuseDesktop
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate
            {
                MessageBox.Show("界面操作出现异常，请关闭客户端后重试。\n错误码：UI_OPERATION_FAILED", "Muse 管理系统", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            bool selfTest = args.Length == 2 && args[0] == "--self-test";
            bool smoke = args.Length == 2 && args[0] == "--smoke-test";
            if (args.Length != 0 && !selfTest && !smoke) { Environment.ExitCode = 2; return; }
            string root = smoke || selfTest ? Path.GetFullPath(args[1]) :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MuseDesktop");
            if ((smoke || selfTest) && Directory.Exists(root)) { Environment.ExitCode = 3; return; }
            using (var hash = SHA256.Create())
            using (var mutex = new Mutex(false, "Local\\MuseDesktop-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(root.ToLowerInvariant()))).Replace("-", "")))
            {
                bool acquired = false;
                try
                {
                    try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired)
                    {
                        MessageBox.Show("客户端已经打开。请使用任务栏中的 Muse 管理系统窗口。", "Muse 管理系统");
                        return;
                    }
                    if (selfTest) { Tests.RunModelTests(root); return; }
                    var store = new AccountStore(root);
                    Application.Run(new MainWindow(store, smoke));
                }
                catch
                {
                    Environment.ExitCode = 1;
                    if (selfTest || smoke)
                    {
                        Directory.CreateDirectory(root);
                        File.WriteAllText(Path.Combine(root, "result.txt"), "FAIL: CLIENT_START_FAILED", Encoding.UTF8);
                    }
                    else MessageBox.Show("客户端无法启动。请检查本地数据目录是否可写、账号文件是否完整。\n错误码：CLIENT_START_FAILED\n不会自动清空账号数据。", "Muse 管理系统", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { if (acquired) mutex.ReleaseMutex(); }
            }
        }
    }
}
