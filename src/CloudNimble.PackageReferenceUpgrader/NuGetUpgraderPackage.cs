//------------------------------------------------------------------------------
// <copyright file="NuGetUpgraderPackage.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.ComponentModel.Design;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using EnvDTE;
using EnvDTE80;
using System.Linq;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;

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

            Logger.Initialize(this, "Upgrade to PackageReferences");

            _commandService = (OleMenuCommandService)GetService(typeof(IMenuCommandService));
            AddCommand(0x0100, (s, e) => { System.Threading.Tasks.Task.Run(() => FixBindingRedirects()); }, CheckFixCommandVisibility);
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
        private void CheckFixCommandVisibility(object sender, EventArgs e)
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
        private void FixBindingRedirects()
        {

            _isProcessing = true;

            var files = ProjectHelpers.GetSelectedItemPaths().Where(c => _fileNames.Contains(Path.GetFileName(c)));

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
                    var fullPath = files.ElementAt(i);

                    //RWM: Start by backing up the files.
                    File.Copy(fullPath, fullPath + ".bak", true);
                    Logger.Log($"Backup created for {fullPath}.");

                    //RWM: Load the files.
                    var config = XDocument.Load(fullPath);

                    var oldBindingRoot = config.Root.Descendants().FirstOrDefault(c => c.Name.LocalName == "assemblyBinding");
                    var oldCount = oldBindingRoot.Elements().Count();

                    foreach (var dependentAssembly in oldBindingRoot.Elements().ToList())
                    {
                        var assemblyIdentity = dependentAssembly.Element(assemblyBindingNs + "assemblyIdentity");
                        var bindingRedirect = dependentAssembly.Element(assemblyBindingNs + "bindingRedirect");

                        if (newBindings.ContainsKey(assemblyIdentity.Attribute("name").Value))
                        {
                            Logger.Log($"Reference already exists for {assemblyIdentity.Attribute("name").Value}. Checking version...");
                            //RWM: We've seen this assembly before. Check to see if we can update the version.
                            var newBindingRedirect = newBindings[assemblyIdentity.Attribute("name").Value].Descendants(assemblyBindingNs + "bindingRedirect").First();
                            var oldVersion = Version.Parse(newBindingRedirect.Attribute("newVersion").Value);
                            var newVersion = Version.Parse(bindingRedirect.Attribute("newVersion").Value);

                            if (newVersion > oldVersion)
                            {
                                newBindingRedirect.ReplaceWith(bindingRedirect);
                                Logger.Log($"Version was newer. Binding updated.");
                            }
                            else
                            {
                                Logger.Log($"Version was the same or older. No update needed. Skipping.");
                            }
                        }
                        else
                        {
                            newBindings.Add(assemblyIdentity.Attribute("name").Value, dependentAssembly);
                        }
                    }

                    //RWM: Add the SortedDictionary items to our new assemblyBindingd element.
                    foreach (var binding in newBindings)
                    {
                        assemblyBindings.Add(binding.Value);
                    }

                    //RWM: Fix up the web.config by adding the new assemblyBindings and removing the old one.
                    oldBindingRoot.AddBeforeSelf(assemblyBindings);
                    oldBindingRoot.Remove();

                    //RWM: Save the config file.
                    if (_dte.SourceControl.IsItemUnderSCC(fullPath) && !_dte.SourceControl.IsItemCheckedOut(fullPath))
                    {
                        _dte.SourceControl.CheckOutItem(fullPath);
                    }
                    config.Save(fullPath);

                    Logger.Log($"Update complete. Result: {oldCount} bindings before, {newBindings.Count} after.");
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
