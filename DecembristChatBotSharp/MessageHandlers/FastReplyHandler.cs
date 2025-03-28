﻿using LanguageExt.UnsafeValueAccess;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.MessageHandlers;

public class FastReplyHandler(AppConfig appConfig, BotClient botClient)
{
    public const string DollarPrefix = "$";
    public const string StickerPrefix = $"{DollarPrefix}sticker_";

    private readonly Map<string, string> _textReply = appConfig.FastReply
        .Where(x => !x.Key.StartsWith(DollarPrefix))
        .ToMap();

    private readonly Map<string, string> _stickerReply = appConfig.FastReply
        .Where(x => x.Key.StartsWith(StickerPrefix))
        .Select(x => (x.Key[StickerPrefix.Length..], x.Value))
        .ToMap();

    public async Task<Unit> Do(
        ChatMessageHandlerParams parameters,
        CancellationToken cancelToken)
    {
        var chatId = parameters.ChatId;
        var telegramId = parameters.TelegramId;
        var messageId = parameters.MessageId;

        var maybeReply = parameters.Payload switch
        {
            TextPayload { Text: var text } => _textReply.Find(text),
            StickerPayload { FileId: var fileId } => _stickerReply.Find(fileId),
            _ => None
        };
        return await maybeReply
            .Map(GetReplyPayload)
            .Map(reply => TrySendReply(chatId, reply, messageId, cancelToken))
            .MapAsync(trySend => LogResult(trySend, chatId, telegramId, maybeReply.ValueUnsafe()!))
            .Value;
    }

    private IMessagePayload GetReplyPayload(string reply) => reply.StartsWith(StickerPrefix)
        ? new StickerPayload(reply[StickerPrefix.Length..])
        : new TextPayload(reply);

    private TryAsync<Message> TrySendReply(
        long chatId,
        IMessagePayload reply,
        int messageId,
        CancellationToken cancellationToken) => TryAsync(reply switch
    {
        StickerPayload { FileId: var fileId } => botClient.SendSticker(
            chatId,
            new InputFileId(fileId),
            replyParameters: new ReplyParameters { MessageId = messageId },
            cancellationToken: cancellationToken),
        TextPayload { Text: var text } => botClient.SendMessage(
            chatId,
            text,
            replyParameters: new ReplyParameters { MessageId = messageId },
            cancellationToken: cancellationToken),
        _ => throw new ArgumentNullException(nameof(reply))
    });

    private Task<Unit> LogResult(
        TryAsync<Message> trySend,
        long chatId,
        long telegramId,
        string payload) => trySend.Match(
        _ => Log.Information("Sent fast reply to {0} payload {1} in chat {2}", telegramId, payload, chatId),
        ex => Log.Error(ex, "Failed to send fast reply to {0} payload {1} in chat {2}", telegramId, payload, chatId)
    );
}