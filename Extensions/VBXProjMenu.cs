using Frosty.Core;
using Frosty.Core.Controls;
using FrostySdk.Managers;
using VBXProj.Parsers;

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
    
    public class VBXExport : DataExplorerContextMenuExtension
    {
        public override string ContextItemName => "Export as VBX";

        public override RelayCommand ContextItemClicked => new RelayCommand(o =>
        {
            FrostySaveFileDialog saveFileDialog = new FrostySaveFileDialog("Export Asset", "VBX Asset (*.vbx)|*.vbx", "");
            if (!saveFileDialog.ShowDialog())
                return;

            VbxDataWriter writer = new VbxDataWriter(saveFileDialog.FileName);
            writer.WriteAsset(App.SelectedAsset, true);
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
            App.EditorWindow.DataExplorer.ShowOnlyModified = false;
            App.EditorWindow.DataExplorer.ShowOnlyModified = true;
            App.EditorWindow.DataExplorer.RefreshAll();
        });
    }
}