//------------------------------------------------------------------------------
// <copyright file="NuGetUpgraderPackage.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CloudNimble.PackageReferenceUpgrader
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // Info on this package for Help/About
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists)]
    [Guid(PackageGuids.guidNuGetUpgraderPackageString)]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    public sealed class NuGetUpgraderPackage : Package
    {

        public DTE2 _dte;
        public static NuGetUpgraderPackage Instance;
        private static bool _isProcessing;
        private OleMenuCommandService _commandService;
        private List<string> _fileNames = new List<string> { "packages.config" };

        /// <summary>
        /// Initializes a new instance of the <see cref="NuGetUpgrader"/> class.
        /// </summary>
        public NuGetUpgraderPackage()
        {
        }

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            _dte = GetService(typeof(DTE)) as DTE2;
            Instance = this;

            Logger.Initialize(this, "PackageReference Upgrade");

            _commandService = (OleMenuCommandService)GetService(typeof(IMenuCommandService));
            AddCommand(0x0100, (s, e) => { System.Threading.Tasks.Task.Run(() => UpgradePackagesConfig()); }, CheckUpgradeCommandVisibility);
        }

        #endregion

        /// <summary>
        /// 
        /// </summary>
        /// <param name="commandId"></param>
        /// <param name="invokeHandler"></param>
        /// <param name="beforeQueryStatus"></param>
        private void AddCommand(int commandId, EventHandler invokeHandler, EventHandler beforeQueryStatus)
        {
            var cmdId = new CommandID(PackageGuids.guidNuGetUpgraderPackageCmdSet, commandId);
            var menuCmd = new OleMenuCommand(invokeHandler, cmdId);
            menuCmd.BeforeQueryStatus += beforeQueryStatus;
            _commandService.AddCommand(menuCmd);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        private void CheckUpgradeCommandVisibility(object sender, EventArgs e)
        {
            OleMenuCommand button = (OleMenuCommand)sender;
            button.Visible = button.Enabled = false;

            if (_dte.SelectedItems.Count != 1)
                return;

            var paths = ProjectHelpers.GetSelectedItemPaths();

            var isWebConfig = paths.Any(c => _fileNames.Contains(Path.GetFileName(c)));
            button.Visible = button.Enabled = isWebConfig;

            if (button.Visible && _isProcessing)
            {
                button.Enabled = false;
                button.Text += " (running)";
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private void UpgradePackagesConfig()
        {

            _isProcessing = true;

            var files = ProjectHelpers.GetSelectedItems().Where(c => _fileNames.Contains(Path.GetFileName(c.GetFullPath())));

            if (!files.Any())
            {
                _dte.StatusBar.Text = "Please select a package.config file to nuke from orbit.";
                _isProcessing = false;
                return;
            }

            //var projectFolder = ProjectHelpers.GetRootFolder(ProjectHelpers.GetActiveProject());
            int count = files.Count();

            //RWM: Don't mess with these.
            XNamespace defaultNs = "http://schemas.microsoft.com/developer/msbuild/2003";
            var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

            try
            {
                string text = count == 1 ? " file" : " files";
                _dte.StatusBar.Progress(true, $"Fixing {count} config {text}...", AmountCompleted: 1, Total: count + 1);

                Parallel.For(0, count, options, i =>
                {
                    var packageReferences = new XElement(defaultNs + "ItemGroup");
                    var packagesConfigItem = files.ElementAt(i);
                    var packagesConfigPath = packagesConfigItem.GetFullPath();
                    var projectPath = packagesConfigItem.ContainingProject.FileName;

                    //RWM: Start by backing up the files.
                    File.Copy(packagesConfigPath, $"{packagesConfigPath}.bak", true);
                    File.Copy(projectPath, $"{projectPath}.bak", true);

                    Logger.Log($"Backup created for {packagesConfigPath}.");

                    //RWM: Load the files.
                    var project = XDocument.Load(projectPath);
                    var packagesConfig = XDocument.Load(packagesConfigPath);

                    //RWM: Get references to the stuff we're gonna get rid of.
                    var oldReferences = project.Root.Descendants().Where(c => c.Name.LocalName == "Reference");
                    var errors = project.Root.Descendants().Where(c => c.Name.LocalName == "Error");
                    var targets = project.Root.Descendants().Where(c => c.Name.LocalName == "Import");

                    foreach (var row in packagesConfig.Root.Elements().ToList())
                    {
                        //RWM: Create the new PackageReference.
                        packageReferences.Add(new XElement(defaultNs + "PackageReference",
                            new XAttribute("Include", row.Attribute("id").Value),
                            new XAttribute("Version", row.Attribute("version").Value)));

                        //RWM: Remove the old Standard Reference.
                        if (oldReferences != null) oldReferences.Where(c => c.Attribute("Include") != null).Where(c => c.Attribute("Include").Value.Split(new Char[] { ',' })[0].ToLower() == row.Attribute("id").Value.ToLower()).ToList()
                            .ForEach(c => c.Remove());

                        //RWM: Remove any remaining Standard References where the PackageId is in the HintPath.
                        if (oldReferences != null) oldReferences.Where(c => c.Descendants().Any(d => d.Value.Contains(row.Attribute("id").Value))).ToList()
                            .ForEach(c => c.Remove());

                        //RWM: Remove any Error conditions for missing Package Targets.
                        if (errors != null) errors.Where(c => c.Attribute("Condition") != null).Where(c => c.Attribute("Condition").Value.Contains(row.Attribute("id").Value)).ToList()
                            .ForEach(c => c.Remove());

                        //RWM: Remove any Package Targets.
                        if (targets != null) targets.Where(c => c.Attribute("Project") != null).Where(c => c.Attribute("Project").Value.Contains(row.Attribute("id").Value)).ToList()
                            .ForEach(c => c.Remove());
                    }

                    //RWM: Fix up the project file by adding PackageReferences, removing packages.config, and pulling NuGet-added Targets.
                    project.Root.Elements().First(c => c.Name.LocalName == "ItemGroup").AddBeforeSelf(packageReferences);
                    var packageConfigReference = project.Root.Descendants().FirstOrDefault(c => c.Name.LocalName == "None" && c.Attribute("Include").Value == "packages.config");
                    if (packageConfigReference != null)
                    {
                        packageConfigReference.Remove();
                    }

                    var nugetBuildImports = project.Root.Descendants().FirstOrDefault(c => c.Name.LocalName == "Target" && c.Attribute("Name").Value == "EnsureNuGetPackageBuildImports");
                    if (nugetBuildImports != null && nugetBuildImports.Descendants().Count(c => c.Name.LocalName == "Error") == 0)
                    {
                        nugetBuildImports.Remove();
                    }

                    //RWM: Upgrade the ToolsVersion so it can't be opened in VS2015 anymore.
                    project.Root.Attribute("ToolsVersion").Value = "15.0";

                    //RWM: Save the project and delete Packages.config.
                    ProjectHelpers.CheckFileOutOfSourceControl(projectPath);
                    ProjectHelpers.CheckFileOutOfSourceControl(packagesConfigPath);
                    project.Save(projectPath, SaveOptions.None);
                    File.Delete(packagesConfigPath);

                    Logger.Log($"Update complete. Visual Studio will prompt you to reload the project now.");
                });
            }
            catch (AggregateException agEx)
            {
                _dte.StatusBar.Progress(false);
                Logger.Log($"Update failed. Exceptions:");
                foreach (var ex in agEx.InnerExceptions)
                {
                    Logger.Log($"Message: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
                }
                _dte.StatusBar.Text = "Operation failed. Please see Output Window for details.";
                _isProcessing = false;

            }
            finally
            {
                _dte.StatusBar.Progress(false);
                _dte.StatusBar.Text = "Operation finished. Please see Output Window for details.";
                _isProcessing = false;
            }

        }

    }

}
