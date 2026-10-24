using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FlightCompanion.XPilot {
  internal sealed class TelegramUpdate {
    internal long Id,ChatId,UserId,UnixSeconds,ReplyToId;
    internal string ChatType,Text;
    internal bool IsBot,Forwarded;
  }
  internal interface ITelegramApi : IDisposable {
    Task<List<TelegramUpdate>> GetUpdatesAsync(long offset,int timeout,CancellationToken cancel);
    Task<long> SendTextAsync(string text,CancellationToken cancel);
  }
  internal sealed class TelegramApi : ITelegramApi {
    readonly HttpClient client;
    readonly string token,chatId;
    internal TelegramApi(string botToken,string allowedChatId,HttpMessageHandler handler=null) {
      token=botToken;chatId=allowedChatId;
      client=new HttpClient(handler??new HttpClientHandler {AllowAutoRedirect=false}) {Timeout=TimeSpan.FromSeconds(35)};
    }
    async Task<JsonElement> RequestAsync(string method,object body,CancellationToken cancel) {
      try {
        using(var request=new HttpRequestMessage(HttpMethod.Post,"https://api.telegram.org/bot"+token+"/"+method)) {
          request.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
          using(var response=await client.SendAsync(request,cancel).ConfigureAwait(false)) {
            if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Telegram HTTP request failed ("+(int)response.StatusCode+").");
            using(var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false))) {
              var payload=document.RootElement;JsonElement ok,result;
              if(payload.ValueKind!=JsonValueKind.Object||!payload.TryGetProperty("ok",out ok)||ok.ValueKind!=JsonValueKind.True||!payload.TryGetProperty("result",out result))
                throw new InvalidOperationException("Telegram rejected the request.");
              return result.Clone();
            }
          }
        }
      }catch(OperationCanceledException){throw;}
      catch {throw new InvalidOperationException("Telegram request failed. Check the token, connectivity and other bot integrations. Request details are not logged.");}
    }
    public async Task<List<TelegramUpdate>> GetUpdatesAsync(long offset,int timeout,CancellationToken cancel) {
      var result=await RequestAsync("getUpdates",new {offset=offset,timeout=timeout,limit=100,allowed_updates=new[]{"message"}},cancel).ConfigureAwait(false);
      return ParseUpdates(result);
    }
    public async Task<long> SendTextAsync(string text,CancellationToken cancel) {
      var result=await RequestAsync("sendMessage",new {chat_id=chatId,text=text},cancel).ConfigureAwait(false);
      JsonElement id;
      if(result.ValueKind!=JsonValueKind.Object||!result.TryGetProperty("message_id",out id)||id.ValueKind!=JsonValueKind.Number||!id.TryGetInt64(out long messageId)||messageId<=0)throw new InvalidOperationException("Telegram returned no message ID.");
      return messageId;
    }
    internal async Task<string> VerifyBotAsync(CancellationToken cancel) {
      var me=await RequestAsync("getMe",new {},cancel).ConfigureAwait(false);
      if(me.ValueKind!=JsonValueKind.Object||!me.TryGetProperty("is_bot",out var isBot)||isBot.ValueKind!=JsonValueKind.True||!me.TryGetProperty("username",out var username)||username.ValueKind!=JsonValueKind.String||
         !Regex.IsMatch(username.GetString()??"",@"^[A-Za-z0-9_]{5,32}$"))throw new InvalidOperationException("Telegram returned an invalid bot identity.");
      var webhook=await RequestAsync("getWebhookInfo",new {},cancel).ConfigureAwait(false);
      if(webhook.ValueKind!=JsonValueKind.Object||!webhook.TryGetProperty("url",out var url)||url.ValueKind!=JsonValueKind.String)throw new InvalidOperationException("Could not check the webhook state.");
      if(!String.IsNullOrEmpty(url.GetString()))throw new InvalidOperationException("This bot has a webhook. Its owner must remove it or use a dedicated bot; no settings were changed.");
      return username.GetString();
    }
    internal static List<TelegramUpdate> ParseUpdates(JsonElement input) {
      var result=new List<TelegramUpdate>();
      if(input.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("Invalid Telegram updates response.");
      foreach(var item in input.EnumerateArray()) {
        if(item.ValueKind!=JsonValueKind.Object||!item.TryGetProperty("update_id",out var id)||id.ValueKind!=JsonValueKind.Number||!id.TryGetInt64(out long updateId))continue;
        var update=new TelegramUpdate{Id=updateId};result.Add(update);
        try {
          var m=item.GetProperty("message");var chat=m.GetProperty("chat");var from=m.GetProperty("from");
          update.ChatId=chat.GetProperty("id").GetInt64();update.ChatType=chat.GetProperty("type").GetString();
          update.UserId=from.GetProperty("id").GetInt64();update.IsBot=from.GetProperty("is_bot").GetBoolean();
          update.UnixSeconds=m.GetProperty("date").GetInt64();
          update.Forwarded=m.TryGetProperty("forward_origin",out _)||m.TryGetProperty("forward_date",out _);
          if(m.TryGetProperty("reply_to_message",out var reply))update.ReplyToId=reply.GetProperty("message_id").GetInt64();
          // Assign text last: malformed sender/date/reply metadata can never become a command.
          update.Text=m.TryGetProperty("text",out var text)&&text.ValueKind==JsonValueKind.String?text.GetString():null;
        }catch {update.Text=null;}
      }
      return result;
    }
    public void Dispose(){client.Dispose();}
  }
}
