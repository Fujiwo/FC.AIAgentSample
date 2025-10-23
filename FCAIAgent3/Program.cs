// 【概要】
// Microsoft.Agents.AI フレームワークを使用した、AI エージェントの実装例
// 指定されたチャットクライアント(Ollama / Azure OpenAI)を利用し、複数ターン対話を実行
//
// 【前提条件】
// - Ollama がインストールされ、http://localhost:11434 で起動していること
// - Ollama でモデル "gpt-oss:20b-cloud" が利用可能であること
// - Azure OpenAI が作成され、エンドポイントと API キーが取得できていること
//
// 【実行方法】
// dotnet run --project FCAIAgent3
//
// 【動作説明】
// 1. チャットクライアント(Ollama / Azure OpenAI)を生成
// 2. ChatClientAgent を作成(エージェント名と指示を設定)
// 3. AgentThread を使った複数ターン対話ループを実行

using System;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OllamaSharp;
// Azure OpenAI のクライアントを利用するための名前空間
using Azure;
using Azure.AI.OpenAI;

const string agentName = "テキストベースRPG";
const string instructions = "あなたは**テキストベースRPGのゲームマスター**です";
// 新: エージェントのシステムロールに与える文脈的な指示
const string systemPrompt = @"
あなたは**テキストベースRPGのゲームマスター**です。
舞台は「ドラゴンクエスト」風の世界――**アレフガルド**。
プレイヤーはホビットの冒険者。
あなたは老魔法使い**ガンダルフ**として同行し、長老口調で語ります。
---
### 🗺 基本ルール
* すべて日本語で進行。
* 各場面で3〜5個の**番号付きコマンド**を提示。
* ステータスや戦闘は**ドラクエ風ウィンドウ形式**で表示。
* 宿屋で回復、モンスターとターン制バトル。
* セーブ＆ロードは「復活の呪文（文字列）」で。
---
### ⚔ 出力例

#### 初回
＝＝＝＝＝＝＝＝＝＝＝＝＝＝＝＝
ガンダルフ：「おお……見知らぬホビットよ。
そなたの名を教えてくれぬかのう？」
＝＝＝＝＝＝＝＝＝＝＝＝＝＝＝＝

#### 以降
ガンダルフ：「{ガンダルフのセリフ}」

[ステータス]
{ プレイヤー名}
Lv{Lv} HP: { HP}
MP: { MP}
G: { Gold}

コマンド：
1. はなす
2. まわりをみる
3. たたかう
4. どうぐ
5. ふっかつのじゅもん
---

ゲーム開始時に、
ガンダルフが「あなたの名前は？」と尋ねて開始してください。
---
";

// 旧: ユーザーからのプロンプトの例
//const string userPrompt   = "「AIエージェント」とはどのようなものですか?";

// 使用するチャットクライアント種別
const ChatClientType chatClientType = ChatClientType.AzureOpenAI;
using IChatClient chatClient = GetChatClient(chatClientType);

// ChatClientAgent の作成 (Agent の名前やインストラクションを指定する)
AIAgent agent = new ChatClientAgent(
    chatClient,
    new ChatClientAgentOptions {
        Name         = agentName,
        Instructions = instructions
    }
);

// 旧: ここから
//try {
//    // エージェントを実行して結果を表示する
//    AgentRunResponse response = await agent.RunAsync(userPrompt);
//    Console.WriteLine(response.Text);
//} catch (Exception ex) {
//    Console.WriteLine($"Error running agent: {ex.Message}");
//}
// 旧: ここまで

// 新: ここから
// 複数ターンに対応するために AgentThread (会話の状態・履歴などを管理) を作成
AgentThread thread = agent.GetNewThread();

// システムメッセージを作成して最初に送信
ChatMessage systemMessage = new(ChatRole.System, systemPrompt);
await RunAsync(agent, systemMessage, thread);

const string exitPrompt = "exit";
Console.WriteLine($"(Interactive chat started. Type '{exitPrompt}' to quit.)\n");

// 対話ループ: ユーザー入力を受け取り exit で終了
for (; ;) {
    var (isValid, userMessage) = GetUserMessage();
    if (!isValid)
        break;
    await RunAsync(agent, userMessage, thread);
}

// エージェントに ChatMessage を投げて応答を取得
static async Task RunAsync(AIAgent agent, ChatMessage chatMessage, AgentThread? thread = null)
{
    try {
        var response = await agent.RunAsync(chatMessage, thread);
        Console.WriteLine($"Agent: {response.Text ?? string.Empty}\n");
    } catch (Exception ex) {
        Console.WriteLine($"Error running agent: {ex.Message}");
    }
}

// コンソールからユーザー入力を読み取り ChatMessage を返す
static (bool isValid, ChatMessage userMessage) GetUserMessage()
{
    var (isValid, userPrompt) = GetUserPrompt();
    return (isValid, new(ChatRole.User, userPrompt));

    static (bool isValid, string userPrompt) GetUserPrompt()
    {
        Console.Write("You: ");
        var userPrompt = Console.ReadLine();
        Console.WriteLine();

        return string.IsNullOrWhiteSpace(userPrompt) ||
               string.Equals(userPrompt.Trim(), exitPrompt, StringComparison.OrdinalIgnoreCase)
            ? (isValid: false, userPrompt: string.Empty)
            : (isValid: true, userPrompt: userPrompt!);
    }
}
// 新: ここまで

// Ollama を使う場合のクライアント生成(ローカルの Ollama サーバーに接続)
static IChatClient GetOllamaClient()
{
    var uri    = new Uri("http://localhost:11434");
    var ollama = new OllamaApiClient(uri);
    // 使用するモデルを指定
    // クラウドベースのモデルを使用(実行速度の向上のため)
    // ローカル LLM を使用する場合は "gemma3:latest" などに変更してください
    ollama.SelectedModel = "gpt-oss:20b-cloud";

    // IChatClient インターフェイスに変換して、ツール呼び出しを有効にしてビルド
    IChatClient chatClient = ollama;
    chatClient = chatClient.AsBuilder()
                           .UseFunctionInvocation() // ツール呼び出しを使う
                           .Build();
    return chatClient;
}

// Azure OpenAI を使う場合のクライアント生成
static IChatClient GetAzureOpenAIClient()
{
    var azureOpenAIEndPoint     = GetEndPoint();
    var openAIApiKey            = GetKey();
    var credential              = new AzureKeyCredential(openAIApiKey);
    // 使用するモデルを指定
    const string deploymentName = "gpt-5-mini";

    var azureOpenAIClient = new AzureOpenAIClient(new Uri(azureOpenAIEndPoint), credential);
    // IChatClient インターフェイスに変換して、ツール呼び出しを有効にしてビルド
    IChatClient chatClient = azureOpenAIClient.GetChatClient(deploymentName)
                                              .AsIChatClient()
                                              .AsBuilder()
                                              .UseFunctionInvocation() // ツール呼び出しを使う
                                              .Build();
    return chatClient;

    static string GetEndPoint()
    {
        const string AzureOpenAIEndpointEnvironmentVariable = "AZURE_OPENAI_ENDPOINT";
        var azureOpenAIEndPoint = Environment.GetEnvironmentVariable(AzureOpenAIEndpointEnvironmentVariable);
        if (string.IsNullOrEmpty(azureOpenAIEndPoint))
            throw new InvalidOperationException($"Please set the {AzureOpenAIEndpointEnvironmentVariable} environment variable.");
        return azureOpenAIEndPoint;

        // 上記のように、セキュリティ上 Azure OpenAI のエンドポイントは環境変数から取得するのが望ましいが、ここではハードコードする
        // 例: 1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef
        //return @"[Azure OpenAI のエンドポイント]";
    }

    static string GetKey()
    {
        const string AzureOpenAIApiKeyEnvironmentVariable = "AZURE_OPENAI_API_KEY";
        var openAIApiKey = Environment.GetEnvironmentVariable(AzureOpenAIApiKeyEnvironmentVariable);
        if (string.IsNullOrEmpty(openAIApiKey))
            throw new InvalidOperationException($"Please set the {AzureOpenAIApiKeyEnvironmentVariable} environment variable.");
        return openAIApiKey!;

        // 上記のように、セキュリティ上 Azure OpenAI の APIキーは環境変数から取得するのが望ましいが、ここではハードコードする
        //例: https://your-resource-name.openai.azure.com/
        //return @"[Azure OpenAI の APIキー]";
    }
}

// ChatClientType に基づいて適切な IChatClient を返すファクトリ関数
static IChatClient GetChatClient(ChatClientType chatClientType)
    => chatClientType switch {
        ChatClientType.Ollama      => GetOllamaClient     (),
        ChatClientType.AzureOpenAI => GetAzureOpenAIClient(),
        _ => throw new NotSupportedException($"Chat client type '{chatClientType}' is not supported.")
    };

// チャットクライアントの種別
enum ChatClientType
{
    AzureOpenAI,
    Ollama
}
