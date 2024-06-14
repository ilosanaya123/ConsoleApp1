using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Data.SQLite;

namespace ConsoleApp1
{
    internal class Program
    {
        static void GetData(string commandline)
        {
            string[] commands = commandline.Split(' ');

            DateTime? dateFrom = null;
            DateTime? dateTo = null;
            string ip = null;
            int? status = null;

            for (int i = 1; i < commands.Length; i++)
            {
                string datetmp = commands[i];

                if (int.TryParse(datetmp, out int st))
                {
                    status = st;
                }
                else if (datetmp.Count(c => c == '.') == 3)
                {
                    ip = datetmp;
                }
                else if (DateTime.TryParseExact(datetmp, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                {
                    if (dateFrom == null)
                    {
                        dateFrom = date;
                    }
                    else
                    {
                        dateTo = date;
                    }
                }
                else
                {
                    Console.WriteLine("Неверный формат данных!");
                    return;
                }
            }

            DataBase.GetLogsByFilter(dateFrom, dateTo, ip, status);
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Добро пожаловать в программу по просмотрк логов!");


            while (true)
            {
                string command = Console.ReadLine();

                if(command == "parse")
                {
                    var logs = Parse("config.txt");

                    if (logs == null)
                    {
                        Console.WriteLine("Не удалось считать данные");
                    }
                    else
                    {
                        DataBase.Create();
                        bool isSuccess = DataBase.SetDatas(logs);
                        if (isSuccess)
                        {
                            Console.WriteLine("Данные успешно получены и записаны в базу данных!");
                            
                        }
                        else
                        {
                            Console.WriteLine("Произошли ошибки при записи данных в базу.");                      
                        }
                    }
                }
                else if(command.StartsWith("get"))
                {
                    GetData(command);
                }
                else
                {
                    Console.WriteLine("Неизвестная команда!");
                }
            }
        }


        public static List<Log> Parse(string configFile)
        {
            try
            {
                var config = Config.LoadFromFile(configFile) ?? throw new Exception("Неправильно задан файл конфигурации!");

                var logFiles = Directory.GetFiles(config.FilesDir, $"*.{config.Ext}");
                if (logFiles.Length == 0)
                {
                    throw new Exception("В выбранной вами папке нет файлов с данным расширением!");
                }

                var result = new List<Log>();

                foreach (var logFile in logFiles)
                {
                    var logLines = File.ReadAllLines(logFile);
                    foreach (var logLine in logLines)
                    {
                        try
                        {
                            var logEntry = Log.Parse(logLine, config.Format);
                            result.Add(logEntry);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Ошибка при обработке строки: {e.Message}");
                        }
                    }
                }

                return result.Count > 0 ? result : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return null;
            }
        }
    }



    class Log
    {
        public Log(Dictionary<string, string> data)
        {
            Data = data;
        }

        public Dictionary<string, string> Data { get; }

        public static Log Parse(string logLine, string format)
        {
            var splits = SplitText(logLine);
            var formats = format.Split(' ').ToList();

            return new Log(GetData(splits, formats));
        }

        static List<string> SplitText(string input)
        {
            return Regex.Matches(input, @"(?:^|\s)(?:""[^""]*""|\[[^\]]*\]|[^\s]+)(?=\s|$)")
                        .Cast<Match>()
                        .Select(m => m.Value.Trim())
                        .ToList();
        }

        static Dictionary<string, string> GetData(List<string> splits, List<string> formats)
        {
            if (splits.Count != formats.Count)
                throw new Exception("Логи не соответствуют формату!");

            var data = new Dictionary<string, string>();

            for (int i = 0; i < formats.Count; i++)
            {
                switch (formats[i])
                {
                    case "%t" when splits[i].StartsWith("["):
                        string dateTimeStr = splits[i].Trim('[', ']');
                        string format = "dd/MMM/yyyy:HH:mm:ss zzz";
                        if (DateTime.TryParseExact(dateTimeStr, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateTime))
                        {
                            data.Add(formats[i], dateTime.ToString("yyyy-MM-dd HH:mm:ss"));
                        }
                        else
                        {
                            throw new Exception("Логи не соответствуют формату!");
                        }
                        break;
                    case "%>s":
                        if (int.TryParse(splits[i], out int status))
                        {
                            data.Add(formats[i], splits[i]);
                        }
                        else
                        {
                            throw new Exception("Логи не соответствуют формату!");
                        }
                        break;
                    case "\\\"%r\\\"" when splits[i].StartsWith("\""):
                        data.Add(formats[i].Replace("\\\"", ""), splits[i].Trim('\"'));
                        break;

                    default:
                        data.Add(formats[i], splits[i]);
                        break;
                }
            }
            return data;
        }
    }


    class Config
    {
        public string FilesDir { get; set; }
        public string Ext { get; set; }
        public string Format { get; set; }

        private Config() { }

        public static Config LoadFromFile(string configPath)
        {
            if (!File.Exists(configPath))
            {
                Console.WriteLine("Файл конфигурации не найден!");
                return null ;
            }

            var config = new Config();
            var lines = File.ReadAllLines(configPath);

            foreach (var line in lines)
            {
                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length != 2) continue;

                var key = parts[0].Trim().ToLower();
                var value = parts[1].Trim();

                switch (key)
                {
                    case "files_dir":
                        config.FilesDir = value;
                        break;
                    case "ext":
                        config.Ext = value;
                        break;
                    case "format":
                        config.Format = value;
                        break;
                }
            }

            if (string.IsNullOrEmpty(config.FilesDir) || string.IsNullOrEmpty(config.Ext) || string.IsNullOrEmpty(config.Format))
            {
                Console.WriteLine("Ошибка: не все необходимые параметры заданы в конфигурационном файле.");
                return null;
            }

            return config;
        }
    }


    class DataBase
    {
        static string databasePath = "logs.db";

        public static void Create()
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }

            SQLiteConnection.CreateFile(databasePath);

            using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
            {
                connection.Open();

                string createTableQuery = @"
                    CREATE TABLE Logs (
                        ip TEXT,
                        dateofrequest DATETIME,
                        request TEXT,
                        status INTEGER
                    )";

                using (var command = new SQLiteCommand(createTableQuery, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void InsertLog(string ip, DateTime dateOfRequest, string request, int status)
        {
            using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
            {
                connection.Open();

                string insertQuery = @"
                    INSERT INTO Logs (ip, dateofrequest, request, status)
                    VALUES (@ip, @dateofrequest, @request, @status)";

                using (var command = new SQLiteCommand(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@ip", ip);
                    command.Parameters.AddWithValue("@dateofrequest", dateOfRequest);
                    command.Parameters.AddWithValue("@request", request);
                    command.Parameters.AddWithValue("@status", status);

                    command.ExecuteNonQuery();
                }
            }
        }

        public static bool SetDatas(List<Log> res)
        {
            bool isSuc = false;
            foreach (Log logEntry in res)
            {
                if (logEntry.Data.ContainsKey("%h") && logEntry.Data.ContainsKey("%t") && logEntry.Data.ContainsKey("%r") && logEntry.Data.ContainsKey("%>s"))
                {
                    isSuc = true;
                    InsertLog(logEntry.Data["%h"], DateTime.Parse(logEntry.Data["%t"]), logEntry.Data["%r"], int.Parse(logEntry.Data["%>s"]));
                }
                else
                {
                    Console.WriteLine("Ошибка при вносе данных!");
                }
            }

            return isSuc;
        }

        public static void GetLogs()
        {
            GetLogsByFilter(null, null, null, null);
        }

        public static void GetLogsByFilter(DateTime? dateFrom, DateTime? dateTo, string ip, int? status)
        {
            using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
            {
                connection.Open();

                string selectQuery = "SELECT ip, dateofrequest, request, status FROM Logs WHERE 1=1";

                if (dateFrom.HasValue && dateTo.HasValue)
                {
                    selectQuery += $" AND dateofrequest BETWEEN '{dateFrom.Value:yyyy-MM-dd}' AND '{dateTo.Value:yyyy-MM-dd}'";
                }
                else if (dateFrom.HasValue)
                {
                    selectQuery += $" AND date(dateofrequest) = '{dateFrom.Value:yyyy-MM-dd}'";
                }
                else if (dateTo.HasValue)
                {
                    selectQuery += $" AND date(dateofrequest) = '{dateTo.Value:yyyy-MM-dd}'";
                }

                if (!string.IsNullOrEmpty(ip))
                {
                    selectQuery += $" AND ip = '{ip}'";
                }

                if (status.HasValue)
                {
                    selectQuery += $" AND status = {status}";
                }

                using (var command = new SQLiteCommand(selectQuery, connection))
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string logIp = reader.GetString(0);
                        DateTime logDateOfRequest = reader.GetDateTime(1);
                        string logRequest = reader.GetString(2);
                        int logStatus = reader.GetInt32(3);

                        Console.WriteLine($"IP: {logIp}, Date: {logDateOfRequest}, Request: {logRequest}, Status: {logStatus}");
                    }
                }
            }
        }
    }
}
