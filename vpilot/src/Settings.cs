using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace FlightCompanion {
  public sealed class PluginSettings {
    public bool Enabled { get; set; }
    public string BotToken { get; set; }
    public string BotTokenEnvironmentVariable { get; set; }
    public string AllowedChatId { get; set; }
    public string AllowedUserId { get; set; }
    public bool AllowReplies { get; set; }
    public bool AllowDisconnect { get; set; }
    public bool ForwardPrivateMessages { get; set; }
    public bool ForwardSelcal { get; set; }
    public bool ForwardRadioMessages { get; set; }
    public bool RadioOnlyWhenCallsignMentioned { get; set; }
    public PluginSettings() {
      BotToken="";BotTokenEnvironmentVariable="FLIGHT_COMPANION_TELEGRAM_TOKEN";
      AllowedChatId=""; AllowedUserId="";
      ForwardPrivateMessages=true; ForwardSelcal=true; RadioOnlyWhenCallsignMentioned=true;
    }
    internal void Validate() {
      long chat,user;
      if(!Regex.IsMatch(AllowedChatId??"",@"^[1-9][0-9]*$")||!Int64.TryParse(AllowedChatId,out chat)||
         !Regex.IsMatch(AllowedUserId??"",@"^[1-9][0-9]*$")||!Int64.TryParse(AllowedUserId,out user))
        throw new InvalidDataException("Explicit private Telegram chat and user IDs are required.");
      if(!String.IsNullOrWhiteSpace(BotToken)) { ValidateToken(BotToken.Trim());return; }
      if(!Regex.IsMatch(BotTokenEnvironmentVariable??"",@"^[A-Za-z_][A-Za-z0-9_]{0,127}$"))
        throw new InvalidDataException("Invalid token environment-variable name.");
    }
    internal string ReadToken() {
      // An explicitly configured JSON token takes precedence. A malformed explicit
      // value fails closed instead of silently authenticating a different bot.
      if(!String.IsNullOrWhiteSpace(BotToken)) {string configured=BotToken.Trim();ValidateToken(configured);return configured;}
      if(!Regex.IsMatch(BotTokenEnvironmentVariable??"",@"^[A-Za-z_][A-Za-z0-9_]{0,127}$"))
        throw new InvalidDataException("Invalid token environment-variable name.");
      string token=Environment.GetEnvironmentVariable(BotTokenEnvironmentVariable);
      ValidateToken(token);
      return token;
    }
    static void ValidateToken(string token) {
      if(!Regex.IsMatch(token??"",@"^[0-9]+:[A-Za-z0-9_-]{20,}$"))
        throw new InvalidDataException("The Telegram bot token is missing or invalid. Set BotToken in JSON or the named environment variable.");
    }
    internal static PluginSettings Load(string path) {
      var result=new JavaScriptSerializer().Deserialize<PluginSettings>(File.ReadAllText(path));
      if(result==null)throw new InvalidDataException("Plugin configuration is empty.");
      return result;
    }
  }
}
