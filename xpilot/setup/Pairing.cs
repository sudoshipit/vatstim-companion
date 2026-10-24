using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FlightCompanion.XPilot.Setup {
  internal sealed class Pairing {
    internal readonly string Phrase="pair "+Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    readonly long started;
    long watermark;
    bool used;
    internal Pairing(long now,long offset){started=now;watermark=offset;}
    internal bool Accept(TelegramUpdate update,long now) {
      if(used||now<started||now-started>=300||update.Id<watermark||update.Id==Int64.MaxValue)return false;
      watermark=update.Id+1;
      if(update.ChatType!="private"||update.ChatId<=0||update.UserId<=0||update.IsBot||update.Forwarded||update.ReplyToId!=0||
        update.UnixSeconds<started||update.UnixSeconds>now+5||now-update.UnixSeconds>300||update.Text!=Phrase)return false;
      used=true;return true;
    }
  }
  internal static class ConfigurationWriter {
    internal static void WriteNew(string path,PluginSettings settings) {
      settings.Validate();settings.ReadToken();
      byte[] bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings,new JsonSerializerOptions{WriteIndented=true})+Environment.NewLine);
      var options=new FileStreamOptions {Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};
      if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
      using(var stream=new FileStream(path,options))stream.Write(bytes);
    }
  }
}
