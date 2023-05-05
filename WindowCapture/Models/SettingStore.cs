using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace WindowCapture.Models {

    public struct SettingData {
        public string video_save_path { get; set; }
    }

    internal class SettingStore {
        private string saveDataDir = $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}/com.ion.windowCapture/WindowCapture";
        private string saveDataFile => $"{saveDataDir}/Settings.json";
        private SettingData settingData = new();

        private static SettingStore instance = new();
        public static SettingStore Instance { get => instance; }

        public SettingStore() {
            if (!Directory.Exists(saveDataDir)) {
                Directory.CreateDirectory(saveDataDir);
            }
            if (!File.Exists(saveDataFile)) {
                SaveVideoPath(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            }
            settingData = JsonSerializer.Deserialize<SettingData>(File.ReadAllText(saveDataFile));
        }



        public string GetVideoPath() {
            return settingData.video_save_path;
        }

        public void SaveVideoPath(string path) {
            settingData.video_save_path = path;
            File.WriteAllText(saveDataFile, JsonSerializer.Serialize(settingData));
        }

        public async Task SaveVideoPathAsync(string path) {
            settingData.video_save_path = path;
            await File.WriteAllTextAsync(saveDataFile, JsonSerializer.Serialize(settingData));
        }
    }
}
