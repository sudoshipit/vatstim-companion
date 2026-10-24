using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FlightCompanion.XPilot.Setup {
  internal static class Program {
    static long Now(){return DateTimeOffset.UtcNow.ToUnixTimeSeconds();}
    static bool Ask(string prompt){Console.Write(prompt+" [y/N]: ");return String.Equals(Console.ReadLine(),"y",StringComparison.OrdinalIgnoreCase);}
    static string ReadSecret() {
      if(Console.IsInputRedirected)throw new InvalidOperationException("Use an interactive terminal for the masked token prompt.");
      var value=new StringBuilder();Console.Write("Bot token (hidden): ");
      while(true){var key=Console.ReadKey(true);if(key.Key==ConsoleKey.Enter)break;if(key.Key==ConsoleKey.Backspace){if(value.Length>0)value.Length--;}
        else if(!Char.IsControl(key.KeyChar))value.Append(key.KeyChar);}
      Console.WriteLine();return value.ToString().Trim();
    }
    static async Task<int> Main(string[] args) {
      if(args.Length!=1||args[0]=="--help"){
        Console.WriteLine("Usage: dotnet FlightCompanion.XPilot.Setup.dll <installed FlightCompanion.XPilot folder>");
        Console.WriteLine("Pairs one private Telegram user/chat and creates a NEW JSON config. Close xPilot first. Requires .NET 10 runtime.");
        return args.Length==1?0:1;
      }
      try {
        string folder=Path.GetFullPath(args[0]);string configPath=Path.Combine(folder,"FlightCompanion.XPilot.json");
        if(!File.Exists(Path.Combine(folder,"FlightCompanion.XPilot.dll")))throw new InvalidOperationException("Copy the release plugin folder into xPilot's client Plugins folder first.");
        if(File.Exists(configPath))throw new InvalidOperationException("Configuration already exists. Edit it manually or back it up and remove it before pairing again. Nothing was overwritten.");
        Console.WriteLine("Close xPilot and any other poller using this bot. This helper never launches xPilot, sends messages, or changes webhooks.");
        if(!Ask("Ready to contact Telegram and pair this bot?"))return 0;
        string token=ReadSecret();var config=new PluginSettings{Enabled=true,BotToken=token,AllowedChatId="1",AllowedUserId="1"};config.Validate();
        using(var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(6)))
        using(var api=new TelegramApi(token,"")) {
          string username=await api.VerifyBotAsync(cancel.Token);
          long offset=0;
          foreach(var update in await api.GetUpdatesAsync(-1,0,cancel.Token))if(update.Id>=offset&&update.Id<Int64.MaxValue)offset=update.Id+1;
          var pairing=new Pairing(Now(),offset);long expires=Now()+300;TelegramUpdate paired=null;
          Console.WriteLine("Open @"+username+" in Telegram. Send this exact text in its PRIVATE chat within five minutes:");
          Console.WriteLine(pairing.Phrase);
          while(Now()<expires&&paired==null){
            foreach(var update in await api.GetUpdatesAsync(offset,20,cancel.Token)){
              if(update.Id>=offset&&update.Id<Int64.MaxValue)offset=update.Id+1;
              if(pairing.Accept(update,Now())){paired=update;break;}
            }
          }
          if(paired==null)throw new InvalidOperationException("Pairing expired. No configuration was saved.");
          config.AllowedChatId=paired.ChatId.ToString(System.Globalization.CultureInfo.InvariantCulture);
          config.AllowedUserId=paired.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture);
          Console.WriteLine("Paired private chat "+config.AllowedChatId+" and user "+config.AllowedUserId+".");
          config.AllowReplies=Ask("Enable replies to recent incoming private messages?");
          config.AllowDisconnect=Ask("Enable /disconnect followed by /confirm?");
          Console.WriteLine("The bot token will be saved in PLAIN TEXT in "+configPath+". Keep this file private and out of GitHub.");
          Console.Write("Type SAVE to create the configuration, or anything else to cancel: ");
          if(Console.ReadLine()!="SAVE"){Console.WriteLine("Cancelled; nothing saved.");return 0;}
          ConfigurationWriter.WriteNew(configPath,config);
        }
        Console.WriteLine("Configuration saved. Start xPilot normally, wait a few seconds, then send a fresh /status to the bot.");
        return 0;
      }catch(OperationCanceledException){Console.Error.WriteLine("Setup timed out or was cancelled. Check whether the configuration was saved before retrying.");return 1;}
      catch(Exception ex) when(ex is InvalidOperationException||ex is System.IO.InvalidDataException){Console.Error.WriteLine(ex.Message);return 1;}
      catch {Console.Error.WriteLine("Setup failed. Check the target folder, permissions and connectivity. Secret-bearing request details were not logged.");return 1;}
    }
  }
}
