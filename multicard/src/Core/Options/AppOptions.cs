namespace MultiCardSync.Core.Options;

/// <summary>MultiCard API sozlamalari (appsettings: "MultiCard").</summary>
public sealed class MultiCardOptions
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Domain { get; set; } = "thenovacore";
    public string ApiBaseUrl { get; set; } = "https://api.multiavto.uz";

    /// <summary>Bo'sh bo'lsa Domain'dan hisoblanadi: https://{Domain}.multiavto.uz</summary>
    public string? TenantUrl { get; set; }

    public List<string> Types { get; set; } = new() { "credit", "debit" };
    public int PageSize { get; set; } = 50;
    public int MaxPages { get; set; } = 20;

    public string ResolvedTenantUrl =>
        string.IsNullOrWhiteSpace(TenantUrl) ? $"https://{Domain}.multiavto.uz" : TenantUrl!;
}

/// <summary>Google Sheet sozlamalari (appsettings: "GoogleSheet").</summary>
public sealed class GoogleSheetOptions
{
    public string SpreadsheetId { get; set; } = "";

    /// <summary>
    /// Yoziladigan varaq NOMI. Ilgari varaq indeks bo'yicha olinardi — hisobchi
    /// varaqlar tartibini o'zgartirgach (2026-07-28), index 1 "Инфо" ma'lumotnomasiga
    /// tushib qoldi va tranzaksiyalar noto'g'ri varaqqa yozildi. Nom bo'yicha topish
    /// shu turdagi buzilishni yopadi: varaq topilmasa — aniq xato beriladi.
    /// </summary>
    public string WorksheetTitle { get; set; } = "Нахд Приход&Расход";

    /// <summary>
    /// ESKI usul: 0-indeksli varaq raqami. Faqat <see cref="WorksheetTitle"/> ataylab
    /// bo'sh qoldirilganda ishlatiladi (moslik uchun). Yangi sozlamalarda ishlatmang.
    /// </summary>
    public int WorksheetIndex { get; set; } = 1;

    /// <summary>Service-account JSON fayli (exe yonida). Alternativa: CredentialsJson.</summary>
    public string CredentialsPath { get; set; } = "creds.json";

    /// <summary>Service-account JSON matni (secret orqali). CredentialsPath'dan ustun.</summary>
    public string? CredentialsJson { get; set; }

    /// <summary>Har sinxrondan keyin "oxirgi sinxron" belgisini yozadimi.</summary>
    public bool WriteHeartbeat { get; set; } = true;

    /// <summary>Heartbeat yoziladigan katak (masalan varaqning L1 katagi).</summary>
    public string HeartbeatCell { get; set; } = "L1";
}

/// <summary>Sinxronlash sozlamalari (appsettings: "Sync").</summary>
public sealed class SyncOptions
{
    /// <summary>Necha soniyada bir MultiCard tekshiriladi.</summary>
    public int PollIntervalSeconds { get; set; } = 300;

    /// <summary>Har so'rovda necha kun orqaga qaraladi (kompyuter o'chiq turgan
    /// davrni quvib yetish uchun kengroq — masalan 14 kun).</summary>
    public int LookbackDays { get; set; } = 14;

    /// <summary>Kompyuter uzoq o'chiq tursa, oxirgi sinxrondan beri o'tgan davr
    /// bu chegaragacha avtomatik qamrab olinadi (juda katta fetch'ni cheklaydi).</summary>
    public int MaxLookbackDays { get; set; } = 90;

    /// <summary>Bundan eski ko'rilgan kalitlar lokal bazadan tozalanadi (LookbackDays'dan katta).</summary>
    public int SeenPruneDays { get; set; } = 60;

    /// <summary>Birinchi ishga tushganda ilovani Windows startup'ga qo'shadimi.</summary>
    public bool RegisterStartup { get; set; } = true;
}
