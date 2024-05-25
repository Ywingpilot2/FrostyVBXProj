using System;
using FrostySdk.Interfaces;

namespace VBXProj.Extensions
{
    public interface IActionExtension
    {
        /// <summary>
        /// The logger for the current task, returns null if not specified.
        /// </summary>
        ILogger CurrentLogger { get; set; }
        Action<VProject> Action { get; }
    }

    /// <summary>
    /// This class provides an interface for plugins to execute actions on VBX Project load
    /// </summary>
    public abstract class LoadActionExtension : IActionExtension
    {
        public ILogger CurrentLogger { get; set; }
        public Action<VProject> Action { get; }
    }
    
    /// <summary>
    /// This class provides an interface for plugins to execute actions on VBX Project Save
    /// </summary>
    public abstract class SaveActionExtension : IActionExtension
    {
        public ILogger CurrentLogger { get; set; }
        public Action<VProject> Action { get; }
    }
}