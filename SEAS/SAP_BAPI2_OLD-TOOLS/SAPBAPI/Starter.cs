using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace FimSync_Ezma
{
    class Starter
    {
        public class RunResult
        {
            private string _output = "";
            public string Output { get { return _output; } }

            private string _error = "";
            public string Error { get { return _error; } }

            private int _code = 0;
            public int Code { get { return _code; } }


            public RunResult(Process p, string output)
            {
                _output = output;
                if (p.StartInfo.RedirectStandardError)
                    _error = p.StandardError.ReadToEnd();
                _code = p.ExitCode;
            }

            public RunResult(Exception e)
            {
                _output = null;
                _error = e.Message;
            }

        }

        /// <summary>
        /// Spustí zadaný program se zadanými argumenty
        /// </summary>
        /// <param name="binaryFileName">Název programu ke spuštění (hledá se v binárním adresáři)</param>
        /// <param name="arguments">Argumenty předané programu</param>
        /// <param name="wait">Zda čekat na dokončení</param>
        /// <param name="impersonate">Pro spuštění programu budou použity údaje KeyRecoveryAgenta</param>
        /// <returns>Vysledek spusteni programu pokud byl povolen parametr wait, jinak prazdny retezec</returns>
        public static RunResult RunBinary(string binaryFilePath, string arguments, bool wait = true)
        {
            string output = "";
            RunResult result = null;
            using (Process p = new Process())
            {


                p.StartInfo = new ProcessStartInfo("cmd", "/c " + binaryFilePath + " " + arguments);
                p.StartInfo.UseShellExecute = true;
                if (wait)
                {
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.RedirectStandardError = true;
                    p.StartInfo.UseShellExecute = false;
                }

                try
                {
                    p.Start();
                    if (wait)
                    {
                        output = p.StandardOutput.ReadToEnd();  // output of cmd
                        output = (output.Length == 0) ? " " : output;
                        p.WaitForExit();


                        result = new RunResult(p, output);
                    }
                }
                catch (Exception e)
                {
                    result = new RunResult(e);
                    //Logger.OnGeneral("Chyba při spouštění programu " + binaryFilePath, e);

                }
            }
            return result;
        }

    }
}
