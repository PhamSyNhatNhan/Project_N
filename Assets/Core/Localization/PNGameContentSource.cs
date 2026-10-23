using System;
using System.Collections.Generic;
using System.IO;
using EveEngine;

// Project_N pack resources are not gameplay JSON documents. Other engine consumers keep their policy.
public class PNGameContentSource : EveDirectoryContentSource
{
    public PNGameContentSource(string baseDirectory,string modsDirectory,IEnumerable<string> enabled):base(baseDirectory,modsDirectory,enabled) { }
    protected override bool IsContentDocument(EveContentPack pack,string file)
    {
        string root=Path.GetFullPath(Path.Combine(pack.Directory,"Localization"))+Path.DirectorySeparatorChar;
        return !Path.GetFullPath(file).StartsWith(root,StringComparison.OrdinalIgnoreCase);
    }
}
