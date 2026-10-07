namespace LocalAI;

/// <summary>
/// Kokoro's voices. A voice's id says its language and gender: <c>bf_emma</c> is a British
/// English female voice.
/// </summary>
public static class KokoroVoiceCatalog
{
    public sealed record Voice(string Id, string Name, string Gender, Language Language);

    /// <param name="Prefix">The first letter of the voice ids in this language.</param>
    /// <param name="Code">BCP 47 tag.</param>
    /// <param name="Name">English name of the language.</param>
    /// <param name="Sample">A sentence to try the voice with.</param>
    public sealed record Language(char Prefix, string Code, string Name, string Sample);

    public static readonly IReadOnlyList<Language> Languages =
    [
        new('a', "en-US", "American English", "Hello! This voice was made on this computer, without sending a single word to the cloud."),
        new('b', "en-GB", "British English", "Hello! This voice was made on this computer, without sending a single word to the cloud."),
        new('e', "es", "Spanish", "¡Hola! Esta voz se creó en este ordenador, sin enviar ni una sola palabra a la nube."),
        new('f', "fr", "French", "Bonjour ! Cette voix a été créée sur cet ordinateur, sans envoyer un seul mot dans le cloud."),
        new('i', "it", "Italian", "Ciao! Questa voce è stata creata su questo computer, senza inviare una sola parola al cloud."),
        new('p', "pt-BR", "Brazilian Portuguese", "Olá! Esta voz foi criada neste computador, sem enviar uma única palavra para a nuvem."),
        new('h', "hi", "Hindi", "नमस्ते! यह आवाज़ इसी कंप्यूटर पर बनाई गई है, क्लाउड पर एक भी शब्द भेजे बिना।"),
        new('j', "ja", "Japanese", "こんにちは。この声は、クラウドに一言も送らずに、このコンピューターで作られました。"),
        new('z', "zh", "Mandarin Chinese", "你好！这个声音是在这台电脑上生成的，没有向云端发送任何一个字。"),
    ];

    private static readonly string[] Ids =
    [
        "af_heart", "af_alloy", "af_aoede", "af_bella", "af_jessica", "af_kore", "af_nicole", "af_nova", "af_river", "af_sarah", "af_sky",
        "am_adam", "am_echo", "am_eric", "am_fenrir", "am_liam", "am_michael", "am_onyx", "am_puck", "am_santa",
        "bf_alice", "bf_emma", "bf_isabella", "bf_lily",
        "bm_daniel", "bm_fable", "bm_george", "bm_lewis",
        "ef_dora", "em_alex", "em_santa",
        "ff_siwis",
        "hf_alpha", "hf_beta", "hm_omega", "hm_psi",
        "if_sara", "im_nicola",
        "jf_alpha", "jf_gongitsune", "jf_nezumi", "jf_tebukuro", "jm_kumo",
        "pf_dora", "pm_alex", "pm_santa",
        "zf_xiaobei", "zf_xiaoni", "zf_xiaoxiao", "zf_xiaoyi", "zm_yunjian", "zm_yunxi", "zm_yunxia", "zm_yunyang",
    ];

    public static readonly IReadOnlyList<Voice> All = Ids.Select(Describe).OfType<Voice>().ToArray();

    /// <summary>The voice with this id, or <c>null</c> if Kokoro has none by that name.</summary>
    public static Voice? Find(string? id) => All.FirstOrDefault(v => v.Id == id);

    private static Voice? Describe(string id)
    {
        if (id is not { Length: > 3 } || id[2] != '_')
            return null;
        var language = Languages.FirstOrDefault(l => l.Prefix == id[0]);
        if (language is null)
            return null;
        var gender = id[1] switch { 'f' => "female", 'm' => "male", _ => "" };
        var name = char.ToUpperInvariant(id[3]) + id[4..];
        return new Voice(id, name, gender, language);
    }
}
