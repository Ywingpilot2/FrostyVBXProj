using Frosty.Core;
using Frosty.Core.Controls;

namespace VBXProj.Extensions
{
    public class VBXProjSave : MenuExtension
    {
        public override string TopLevelMenuName => "Tools";
        public override string SubLevelMenuName => "VBX Project";
        public override string MenuItemName => "Save VBX Project";

        public override RelayCommand MenuItemClicked => new RelayCommand(o =>
        {
            // If the current project is a new project, then we need to determine a save location
            FrostySaveFileDialog saveFileDialog = new FrostySaveFileDialog("Save VProject", "VBX Project (*.vproj)|*.vproj", "");
            if (!saveFileDialog.ShowDialog())
                return;
            
            VBXProject.Save(saveFileDialog.FileName);
            App.EditorWindow.DataExplorer.RefreshAll();
        });
    }

    public class VBXProjLoad : MenuExtension
    {
        public override string TopLevelMenuName => "Tools";
        public override string SubLevelMenuName => "VBX Project";
        public override string MenuItemName => "Load VBX Project";

        public override RelayCommand MenuItemClicked => new RelayCommand(o =>
        {
            FrostyOpenFileDialog ofd = new FrostyOpenFileDialog("Open Vproject", "VBX Project (*.vproj)|*.vproj", "");
            if (!ofd.ShowDialog()) return;
            
            App.AssetManager.Reset();
            App.WhitelistedBundles.Clear();
            
            VBXProject.Load(ofd.FileName);
            App.EditorWindow.DataExplorer.RefreshAll();
        });
    }
}