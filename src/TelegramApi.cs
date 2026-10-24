using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FlightCompanion {
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
    internal TelegramApi(string botToken,string allowedChatId) {
      token=botToken; chatId=allowedChatId;
      client=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) {Timeout=TimeSpan.FromSeconds(35)};
    }
    async Task<object> RequestAsync(string method,object body,CancellationToken cancel) {
      var serializer=new JavaScriptSerializer();
      using(var request=new HttpRequestMessage(HttpMethod.Post,"https://api.telegram.org/bot"+token+"/"+method)) {
        request.Content=new StringContent(serializer.Serialize(body),Encoding.UTF8,"application/json");
        using(var response=await client.SendAsync(request,cancel).ConfigureAwait(false)) {
          // Never include the URL, response body, token or message text in errors/logs.
          if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Telegram HTTP request failed ("+(int)response.StatusCode+").");
          var payload=serializer.DeserializeObject(await response.Content.ReadAsStringAsync().ConfigureAwait(false)) as Dictionary<string,object>;
          if(payload==null||!payload.ContainsKey("ok")||!(payload["ok"] is bool)||!(bool)payload["ok"]||!payload.ContainsKey("result"))
            throw new InvalidOperationException("Telegram rejected the request.");
          return payload["result"];
        }
      }
    }
    public async Task<List<TelegramUpdate>> GetUpdatesAsync(long offset,int timeout,CancellationToken cancel) {
      object result=await RequestAsync("getUpdates",new {offset=offset,timeout=timeout,limit=100,allowed_updates=new[]{"message"}},cancel).ConfigureAwait(false);
      return ParseUpdates(result);
    }
    public async Task<long> SendTextAsync(string text,CancellationToken cancel) {
      var result=await RequestAsync("sendMessage",new {chat_id=chatId,text=text},cancel).ConfigureAwait(false) as Dictionary<string,object>;
      if(result==null||!result.ContainsKey("message_id"))throw new InvalidOperationException("Telegram returned no message ID.");
      return Convert.ToInt64(result["message_id"]);
    }
    internal static List<TelegramUpdate> ParseUpdates(object input) {
      var result=new List<TelegramUpdate>();var items=input as object[];
      if(items==null)throw new InvalidOperationException("Invalid Telegram updates response.");
      foreach(object item in items) {
        var v=item as Dictionary<string,object>; if(v==null||!v.ContainsKey("update_id"))continue;
        var update=new TelegramUpdate();
        try { update.Id=Convert.ToInt64(v["update_id"]); } catch { continue; }
        // Preserve every valid update ID, even for unsupported/malformed event types.
        result.Add(update);
        try {
          var m=(Dictionary<string,object>)v["message"];
          var chat=(Dictionary<string,object>)m["chat"];var from=(Dictionary<string,object>)m["from"];
          update.ChatId=Convert.ToInt64(chat["id"]);update.ChatType=chat["type"] as string;
          update.UserId=Convert.ToInt64(from["id"]);update.IsBot=from.ContainsKey("is_bot")&&Convert.ToBoolean(from["is_bot"]);
          update.UnixSeconds=Convert.ToInt64(m["date"]);update.Text=m.ContainsKey("text")?m["text"] as string:null;
          update.Forwarded=m.ContainsKey("forward_origin")||m.ContainsKey("forward_date");
          if(m.ContainsKey("reply_to_message")) {
            var reply=m["reply_to_message"] as Dictionary<string,object>;
            if(reply!=null&&reply.ContainsKey("message_id"))update.ReplyToId=Convert.ToInt64(reply["message_id"]);
          }
        } catch { update.Text=null; }
      }
      return result;
    }
    public void Dispose(){client.Dispose();}
  }
}
