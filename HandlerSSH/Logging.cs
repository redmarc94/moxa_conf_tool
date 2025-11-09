using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace HandlerSSH;

public class Logging
{
    public bool firstlog = true;
    public string logname = string.Empty;

    public Logging(string name)
    {
        logname = name;
    }

    public void Write(string message)
    {
        Console.WriteLine(message);

        try
        {
            string folder = AppDomain.CurrentDomain.BaseDirectory;
            string logFolder = Path.Combine(folder, "Logs");
            Directory.CreateDirectory(logFolder);

            string logFile = Path.Combine(logFolder, $"{logname}.txt");
            string timestamp = DateTime.Now.ToString("dd.MM.yy:HH.mm.ss");
            string line = $"[{timestamp}] {message}";

            if (firstlog)
            {
                firstlog = false;
                if (File.Exists(logFile))
                {
                    try
                    {
                        string? lastLine = File.ReadLines(logFile).LastOrDefault();
                        if (!string.IsNullOrWhiteSpace(lastLine))
                        {
                            int closingBracket = lastLine.IndexOf(']');
                            if (closingBracket > 1)
                            {
                                string datePart = lastLine.Substring(1, closingBracket - 1);
                                if (DateTime.TryParseExact(datePart, "dd.MM.yy:HH.mm.ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime lastDate))
                                {
                                    if (lastDate.Date < DateTime.Now.Date)
                                    {
                                        string backupName = Path.Combine(logFolder, $"{lastDate:ddMMyyyy}_{logname}.txt");
                                        File.Copy(logFile, backupName, true);
                                        File.Delete(logFile);
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignora problemi nella lettura/archiviazione del log esistente
                    }
                }
            }

            File.AppendAllText(logFile, line + Environment.NewLine);
        }
        catch
        {
            // Ignora problemi di IO per evitare crash dell'applicazione
        }
    }
}
