using System.Collections.Generic;
using System.IO;
using System.Linq;
using MDTPro.Setup;
using MDTPro.Utility;

namespace MDTPro.Plugins {
    internal class PluginController {
        internal static List<PluginInfo> GetPlugins() {
            List<PluginInfo> plugins = new List<PluginInfo>();
            DirectoryInfo directoryInfo = new DirectoryInfo(SetupController.PluginsPath);
            List<DirectoryInfo> pluginPaths = directoryInfo.GetDirectories().ToList();

            foreach (DirectoryInfo pluginPath in pluginPaths) {
                PluginInfo plugin = Helper.ReadFromJsonFile<PluginInfo>($"{SetupController.PluginsPath}/{pluginPath.Name}/info.json");

                if (plugin == null) continue;

                string pagesPath = $"{SetupController.PluginsPath}/{pluginPath.Name}/pages";
                string scriptsPath = $"{SetupController.PluginsPath}/{pluginPath.Name}/scripts";
                string stylesPath = $"{SetupController.PluginsPath}/{pluginPath.Name}/styles";

                if (Directory.Exists(pagesPath)) { 
                    plugin.pages = new DirectoryInfo(pagesPath).GetFiles().Select(item => item.Name).ToList();
                }
                if (Directory.Exists(scriptsPath)) {
                    plugin.scripts = new DirectoryInfo(scriptsPath).GetFiles().Select(item => item.Name).ToList();
                }
                if (Directory.Exists(stylesPath)) {
                    plugin.styles = new DirectoryInfo(stylesPath).GetFiles().Select(item => item.Name).ToList();
                }

                plugin.id = pluginPath.Name;

                plugins.Add(plugin);
            }

            return plugins;
        }
    }
}
