using OurTaiko.LanguageServer;

// tja-language-server: LSP over stdin and stdout.
return await new Server(Console.OpenStandardInput(), Console.OpenStandardOutput()).RunAsync();
