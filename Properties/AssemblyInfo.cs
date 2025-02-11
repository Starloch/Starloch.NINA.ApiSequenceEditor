﻿using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// [MANDATORY] The following GUID is used as a unique identifier of the plugin. Generate a fresh one for your plugin!
[assembly: Guid("059b03db-dc64-4024-b78f-63913a55b14a")]

// [MANDATORY] The assembly versioning
//Should be incremented for each new release build of a plugin
[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]

// [MANDATORY] The name of your plugin
[assembly: AssemblyTitle("API Sequence Editor")]
// [MANDATORY] A short description of your plugin
[assembly: AssemblyDescription("Sequence editor for Web Apps")]

// The following attributes are not required for the plugin per se, but are required by the official manifest meta data

// Your name
[assembly: AssemblyCompany("Starloch")]
// The product name that this plugin is part of
[assembly: AssemblyProduct("API Sequence Editor")]
[assembly: AssemblyCopyright("Copyright © 2025 Starloch")]

// The minimum Version of N.I.N.A. that this plugin is compatible with
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.1.2.9001")]

// The license your plugin code is using
[assembly: AssemblyMetadata("License", "MPL-2.0")]
// The url to the license
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
// The repository where your pluggin is hosted
[assembly: AssemblyMetadata("Repository", "https://github.com/Starloch/Starloch.NINA.ApiSequenceEditor")]

// The following attributes are optional for the official manifest meta data

//[Optional] Your plugin homepage URL - omit if not applicaple
[assembly: AssemblyMetadata("Homepage", "https://www.starloch.com")]

//[Optional] Common tags that quickly describe your plugin
[assembly: AssemblyMetadata("Tags", "Web,API,Sequencer")]

//[Optional] A link that will show a log of all changes in between your plugin's versions
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/Starloch/Starloch.NINA.ApiSequenceEditor/CHANGELOG.md")]

//[Optional] The url to a featured logo that will be displayed in the plugin list next to the name
[assembly: AssemblyMetadata("FeaturedImageURL", "https://starloch.com/wp-content/uploads/2024/11/nessyLogoWhiteBorder-1.png")]
//[Optional] A url to an example screenshot of your plugin in action
[assembly: AssemblyMetadata("ScreenshotURL", "https://starloch.com/wp-content/gallery/starloch-all/Sharpless_103_Cygnus_Loop_11-14-2024.jpg")]
//[Optional] An additional url to an example example screenshot of your plugin in action
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
//[Optional] An in-depth description of your plugin
[assembly: AssemblyMetadata("LongDescription", @"Allows the editing of advance sequences through Web Apps

# Features #
* Coming soon

# Getting Help #

If you have question/issues/feedback, you can create an issue [here](https://github.com/Starloch/Starloch.NINA.ApiSequenceEditor/blob/main/CHANGELOG.md)
* This Plugin is provided 'as is' under the terms of the [Mozilla Public License 2.0](https://github.com/Starloch/Starloch.NINA.ApiSequenceEditor/blob/main/LICENSE)
* Source code for this plugin is available at this plugin's [repository](https://github.com/Starloch/Starloch.NINA.ApiSequenceEditor)
")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]
// [Unused]
[assembly: AssemblyConfiguration("")]
// [Unused]
[assembly: AssemblyTrademark("")]
// [Unused]
[assembly: AssemblyCulture("")]