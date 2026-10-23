#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine.Networking;

// The URL path is also exercised in the desktop Player with file:// URLs. Android
// passes its jar:file:// StreamingAssets root; this does not enumerate APK contents.
public static class PNLocalizationJsonReader
{
    public static IEnumerator Read(string root,string file,Action<string> success,Action<Exception> failure)
    {
        string name=null;
        try{name=PNLocalizationCatalog.RelativeFile(file);}catch(Exception e){failure(e);yield break;}
        using(var request=UnityWebRequest.Get(root.Replace('\\','/').TrimEnd('/')+"/"+name))
        {
            request.timeout=15;yield return request.SendWebRequest();
            if(request.result!=UnityWebRequest.Result.Success){failure(new IOException("Localization read failed: "+file+" "+request.error));yield break;}
            try{if(request.downloadHandler.data.Length>PNLocalizationCatalog.MaxFileBytes)throw new FormatException("Localization file too large.");success(new UTF8Encoding(false,true).GetString(request.downloadHandler.data));}catch(Exception e){failure(e);}
        }
    }
}
#endif
