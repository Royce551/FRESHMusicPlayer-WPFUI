﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FRESHMusicPlayer.Handlers.Configuration
{
    public class ConfigurationHandler
    {
        private static string savePath;

        static ConfigurationHandler()
        {
            savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FRESHMusicPlayer", "Configuration", "FMP-Avalonia");
        }


        public static async Task<ConfigurationFile> Read()
        {
            if (!File.Exists(Path.Combine(savePath, "config.json")))
            {
                await Write(new ConfigurationFile());
            }
            using StreamReader file = File.OpenText(Path.Combine(savePath, "config.json"));
            var jsonSerializer = new JsonSerializer();
            return (ConfigurationFile)jsonSerializer.Deserialize(file, typeof(ConfigurationFile));
        }

        public static async Task Write(ConfigurationFile config)
        {
            if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
            using StreamWriter file = File.CreateText(Path.Combine(savePath, "config.json"));
            new JsonSerializer().Serialize(file, config);
        }
    }
}
