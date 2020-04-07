using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace ES.SFTP.Host.Business.Interop
{
    public class ProcessUtil
    {
        public static Task<ProcessRunOutput> QuickRun(string filename, string arguments = null,
            bool throwOnError = true)
        {
            var outputStringBuilder = new StringBuilder();
            var process = new Process
            {
                StartInfo =
                {
                    FileName = filename,
                    Arguments = arguments ?? string.Empty,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.OutputDataReceived += (_, e) => outputStringBuilder.Append(e.Data);
            process.ErrorDataReceived += (_, e) => outputStringBuilder.Append(e.Data);
            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
            }
            catch (Exception)
            {
                if (throwOnError) throw;
            }

            var output = outputStringBuilder.ToString();
            if (process.ExitCode != 0 && throwOnError)
                throw new Exception(
                    $"Process failed with exit code '{process.ExitCode}.{Environment.NewLine}{output}'");
            return Task.FromResult(new ProcessRunOutput
            {
                ExitCode = process.ExitCode,
                Output = output
            });
        }
    }
}