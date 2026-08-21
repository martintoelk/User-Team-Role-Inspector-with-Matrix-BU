using System.ComponentModel.Composition;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace UserTeamRoleInspector
{
    // This is the entry point XrmToolBox discovers via MEF. It just hands back the UI control.
    [Export(typeof(IXrmToolBoxPlugin))]
    [ExportMetadata("Name", "User/Team Role Inspector")]
    [ExportMetadata("Description", "Read-only inspector: pick a user, see every security role they effectively hold, direct or via team membership, with the owning business unit for each.")]
    [ExportMetadata("SmallImageBase64", "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAGUlEQVR42mOwyVv1nxLMMGrAqAGjBgwXAwCgSVMfV4IHbQAAAABJRU5ErkJggg==")]
    [ExportMetadata("BigImageBase64", "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAL0lEQVR42u3OIQEAAAgDMIKRiWCUhBg3E/Ornr2kEhAQEBAQEBAQEBAQEBAQSAceKmxMiOyqBCMAAAAASUVORK5CYII=")]
    [ExportMetadata("BackgroundColor", "White")]
    [ExportMetadata("PrimaryFontColor", "Black")]
    [ExportMetadata("SecondaryFontColor", "DarkGray")]
    public class UserTeamRoleInspectorPlugin : PluginBase
    {
        public override IXrmToolBoxPluginControl GetControl()
        {
            return new UserTeamRoleInspectorControl();
        }
    }
}
