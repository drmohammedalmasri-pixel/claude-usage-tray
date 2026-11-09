// All user-facing text. Order of each entry: ar, en, zh, es, fr, de.
// To add a language: add its code + name below and one more string to every entry.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

public static class L
{
    public static readonly string[] Codes = { "ar", "en", "zh", "es", "fr", "de" };
    public static readonly string[] Names = { "العربية", "English", "中文", "Español", "Français", "Deutsch" };
    static readonly string[] Cultures = { "ar-SA", "en-US", "zh-CN", "es-ES", "fr-FR", "de-DE" };
    static int i = 1;

    public static string Code { get { return Codes[i]; } }
    public static bool Rtl { get { return i == 0; } }
    public static RightToLeft Dir { get { return Rtl ? RightToLeft.Yes : RightToLeft.No; } }

    // pref: language code, or empty = Windows display language (English when not supported)
    public static void Set(string pref)
    {
        string c = string.IsNullOrEmpty(pref) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : pref;
        i = Array.IndexOf(Codes, c);
        if (i < 0) i = 1;
    }

    public static string T(string key, params object[] a)
    {
        string[] v;
        if (!D.TryGetValue(key, out v)) return key;
        return a.Length > 0 ? string.Format(v[i], a) : v[i];
    }

    public static string Pct(double v) { return Math.Round(v).ToString(CultureInfo.InvariantCulture) + (Rtl ? "٪" : "%"); }
    public static string PctN(string n) { return n + (Rtl ? "٪" : "%"); }

    public static string DayName(int dow)
    {
        if (Rtl) return new[] { "الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت" }[dow];
        return new CultureInfo(Cultures[i]).DateTimeFormat.GetDayName((DayOfWeek)dow);
    }

    // ----- counted words -----
    static string Unit(int n, string ar1, string ar2, string arFew, string arMany, string en1, string enN, string zh, string es1, string esN, string fr1, string frN, string de1, string deN)
    {
        switch (Code)
        {
            case "ar": return n == 1 ? ar1 : n == 2 ? ar2 : n <= 10 ? n + " " + arFew : n + " " + arMany;
            case "zh": return n + zh;
            case "es": return n + " " + (n == 1 ? es1 : esN);
            case "fr": return n + " " + (n == 1 ? fr1 : frN);
            case "de": return n + " " + (n == 1 ? de1 : deN);
            default: return n + " " + (n == 1 ? en1 : enN);
        }
    }
    // dat: German dative ("vor 2 Tagen", "in 2 Tagen")
    public static string Days(int n, bool dat) { return Unit(n, "يوم", "يومين", "أيام", "يوماً", "day", "days", " 天", "día", "días", "jour", "jours", "Tag", dat ? "Tagen" : "Tage"); }
    public static string Hours(int n) { return Unit(n, "ساعة", "ساعتين", "ساعات", "ساعة", "hour", "hours", " 小时", "hora", "horas", "heure", "heures", "Stunde", "Stunden"); }
    public static string Mins(int n) { return Unit(n, "دقيقة", "دقيقتين", "دقائق", "دقيقة", "minute", "minutes", " 分钟", "minuto", "minutos", "minute", "minutes", "Minute", "Minuten"); }
    public static string Sessions(int n) { return Code == "ar" && n == 1 ? "جلسة واحدة" : Unit(n, "جلسة", "جلستين", "جلسات", "جلسة", "session", "sessions", " 个会话", "sesión", "sesiones", "session", "sessions", "Sitzung", "Sitzungen"); }

    public static string Span(TimeSpan t, bool dat = false)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        int d = (int)t.TotalDays, h = t.Hours, m = t.Minutes;
        string and = Rtl ? " و" : Code == "zh" ? " " : " ";
        if (d > 0) return Days(d, dat) + (h > 0 ? and + Hours(h) : "");
        if (h > 0) return Hours(h) + (m > 0 ? and + Mins(m) : "");
        return Mins(Math.Max(1, m));
    }

    // full sessions, rounded to halves ("2½ full sessions")
    public static string FullSessions(double v)
    {
        double r = Math.Round(v * 2) / 2;
        int whole = (int)Math.Floor(r);
        bool half = r - whole >= 0.5;
        if (Rtl)
        {
            if (r < 0.5) return "أقل من نصف جلسة";
            if (whole == 0) return "نصف جلسة";
            string w = whole == 1 ? "جلسة" : whole == 2 ? "جلستان" : whole <= 10 ? whole + " جلسات" : whole + " جلسة";
            return (half ? w + " ونصف" : (whole == 1 ? "جلسة واحدة" : w)) + " كاملة";
        }
        if (r < 0.5) return T("less_half");
        string num = r.ToString("0.#", CultureInfo.InvariantCulture);
        if (Code == "fr" || Code == "de" || Code == "es") num = num.Replace('.', ',');
        return T(r <= 1 ? "full_one" : "full_n", num);
    }

    static readonly Dictionary<string, string[]> D = new Dictionary<string, string[]>
    {
        // ----- server / sign-in messages -----
        { "refresh_wait", new[] { "تجديد الدخول مؤجل، إعادة المحاولة بعد قليل", "Sign-in refresh postponed, retrying shortly", "登录刷新已推迟，稍后重试", "Renovación de sesión aplazada, se reintentará en breve", "Renouvellement de la connexion reporté, nouvel essai sous peu", "Anmeldung erneuern verschoben, neuer Versuch in Kürze" } },
        { "no_login", new[] { "لا يوجد دخول محفوظ، أدخل النسبة يدوياً", "No saved sign-in, enter the percentage manually", "没有已保存的登录，请手动输入百分比", "No hay sesión guardada, introduce el porcentaje manualmente", "Aucune connexion enregistrée, saisissez le pourcentage manuellement", "Keine gespeicherte Anmeldung, Prozentwert manuell eingeben" } },
        { "refresh_429", new[] { "تجديد الدخول مؤجل لكثرة الطلبات، إعادة المحاولة لاحقاً", "Too many requests, sign-in refresh retried later", "请求过多，稍后重新刷新登录", "Demasiadas solicitudes, la sesión se renovará más tarde", "Trop de requêtes, renouvellement de la connexion plus tard", "Zu viele Anfragen, Anmeldung wird später erneuert" } },
        { "login_expired", new[] { "انتهى الدخول المحفوظ، سجّل الدخول من جديد وسيعود التحديث وحده", "Saved sign-in expired. Sign in again and updates resume by themselves", "已保存的登录已过期。重新登录后会自动恢复更新", "La sesión guardada caducó. Inicia sesión de nuevo y se reanudará sola", "La connexion enregistrée a expiré. Reconnectez-vous et la mise à jour reprendra seule", "Gespeicherte Anmeldung abgelaufen. Neu anmelden, danach läuft es von selbst weiter" } },
        { "refresh_fail", new[] { "تعذّر تجديد الدخول", "Could not refresh sign-in", "无法刷新登录", "No se pudo renovar la sesión", "Impossible de renouveler la connexion", "Anmeldung konnte nicht erneuert werden" } },
        { "no_cred", new[] { "لم أجد دخول Claude Code، أدخل النسبة يدوياً", "Claude Code sign-in not found, enter the percentage manually", "未找到 Claude Code 登录，请手动输入百分比", "No se encontró la sesión de Claude Code, introduce el porcentaje manualmente", "Connexion Claude Code introuvable, saisissez le pourcentage manuellement", "Claude-Code-Anmeldung nicht gefunden, Prozentwert manuell eingeben" } },
        { "busy", new[] { "الخادم مشغول، إعادة المحاولة لاحقاً", "Server busy, retrying later", "服务器繁忙，稍后重试", "Servidor ocupado, se reintentará más tarde", "Serveur occupé, nouvel essai plus tard", "Server ausgelastet, neuer Versuch später" } },
        { "read_fail", new[] { "تعذّر قراءة الاستهلاك", "Could not read usage", "无法读取用量", "No se pudo leer el uso", "Impossible de lire l’utilisation", "Verbrauch konnte nicht gelesen werden" } },
        { "offline", new[] { "لا يوجد اتصال", "No connection", "无网络连接", "Sin conexión", "Pas de connexion", "Keine Verbindung" } },

        // ----- popup -----
        { "title", new[] { "استهلاك كلود الأسبوعي", "Claude weekly usage", "Claude 每周用量", "Uso semanal de Claude", "Utilisation hebdomadaire de Claude", "Claude-Wochenverbrauch" } },
        { "loading", new[] { "جارٍ جلب البيانات…", "Loading data…", "正在获取数据…", "Cargando datos…", "Chargement des données…", "Daten werden geladen…" } },
        { "usage", new[] { "الاستهلاك", "Usage", "用量", "Uso", "Utilisation", "Verbrauch" } },
        { "time", new[] { "الوقت", "Time", "时间", "Tiempo", "Temps", "Zeit" } },
        { "five", new[] { "حصة الساعات الخمس", "5-hour limit", "5 小时额度", "Límite de 5 horas", "Limite de 5 heures", "5-Stunden-Limit" } },
        { "opus", new[] { "حصة النموذج الأكبر", "Largest model", "最大模型额度", "Modelo más grande", "Plus grand modèle", "Größtes Modell" } },
        { "remaining", new[] { "باقي {0}، التجديد {1} {2}", "{0} left, resets {1} {2}", "剩余 {0}，{1} {2} 重置", "Quedan {0}, se reinicia el {1} a las {2}", "Encore {0}, réinitialisation {1} à {2}", "Noch {0}, Reset {1} {2}" } },
        { "manual_note", new[] { "قيمة يدوية، أُدخلت الساعة {0}", "Manual value, entered at {0}", "手动数值，于 {0} 输入", "Valor manual, introducido a las {0}", "Valeur manuelle, saisie à {0}", "Manueller Wert, eingegeben um {0}" } },
        { "stale", new[] { "آخر تحديث ناجح قبل {0}", "Last successful update {0} ago", "上次成功更新于 {0}前", "Última actualización correcta hace {0}", "Dernière mise à jour réussie il y a {0}", "Letzte erfolgreiche Aktualisierung vor {0}" } },
        { "ahead", new[] { "متقدّم على الوقت بـ {0}", "Ahead of time by {0}", "比时间超前 {0}", "Adelantado al tiempo en {0}", "En avance sur le temps de {0}", "{0} vor der Zeit" } },
        { "behind", new[] { "أقل من الوقت بـ {0}، عندك مجال", "{0} behind time, you have room", "比时间落后 {0}，还有余量", "{0} por debajo del tiempo, tienes margen", "{0} en dessous du temps, vous avez de la marge", "{0} hinter der Zeit, du hast Spielraum" } },
        { "ontrack", new[] { "ماشي مع الوقت تماماً", "Right on track with time", "与时间完全同步", "Justo al ritmo del tiempo", "Parfaitement dans les temps", "Genau im Zeitplan" } },
        { "used_all", new[] { "استهلكت حصة الأسبوع كلها", "You have used the whole weekly limit", "本周额度已全部用完", "Has usado todo el límite semanal", "Vous avez utilisé toute la limite hebdomadaire", "Wochenlimit komplett verbraucht" } },
        { "left_until", new[] { "باقي لك {0} حتى التجديد", "{0} left until the reset", "距重置还剩 {0}", "Te queda {0} hasta el reinicio", "Il vous reste {0} avant la réinitialisation", "Noch {0} bis zum Reset" } },
        { "per_day", new[] { "تقدر تستعمل {0} يومياً حتى التجديد", "You can use {0} per day until the reset", "重置前每天可用 {0}", "Puedes usar {0} al día hasta el reinicio", "Vous pouvez utiliser {0} par jour jusqu’à la réinitialisation", "Du kannst bis zum Reset {0} pro Tag nutzen" } },
        { "five_reset", new[] { "تتجدد بعد {0}", "Resets in {0}", "{0}后重置", "Se reinicia en {0}", "Réinitialisation dans {0}", "Reset in {0}" } },
        { "sessions_left", new[] { "باقي لك تقريباً {0} حتى التجديد", "About {0} left until the reset", "距重置约剩 {0}", "Quedan unas {0} hasta el reinicio", "Il reste environ {0} avant la réinitialisation", "Bis zum Reset bleiben etwa {0}" } },
        { "less_half", new[] { "", "less than half a session", "不到半个会话", "menos de media sesión", "moins d’une demi-session", "weniger als eine halbe Sitzung" } },
        { "full_one", new[] { "", "{0} full session", "{0} 个完整会话", "{0} sesión completa", "{0} session complète", "{0} volle Sitzung" } },
        { "full_n", new[] { "", "{0} full sessions", "{0} 个完整会话", "{0} sesiones completas", "{0} sessions complètes", "{0} volle Sitzungen" } },
        { "ratio", new[] { "جلسة الخمس ساعات الكاملة ≈ {0} من الأسبوع، متوسط {1}", "A full 5-hour session ≈ {0} of the week, average of {1}", "一个完整 5 小时会话 ≈ 每周的 {0}，{1}的平均值", "Una sesión completa de 5 h ≈ {0} de la semana, promedio de {1}", "Une session complète de 5 h ≈ {0} de la semaine, moyenne de {1}", "Eine volle 5-Stunden-Sitzung ≈ {0} der Woche, Schnitt aus {1}" } },
        { "ratio_now", new[] { "هذه الجلسة حتى الآن: ‎+{0} من الأسبوع", "This session so far: +{0} of the week", "本次会话目前：本周的 +{0}", "Esta sesión hasta ahora: +{0} de la semana", "Cette session jusqu’ici : +{0} de la semaine", "Diese Sitzung bisher: +{0} der Woche" } },
        { "ratio_wait", new[] { "نصيب جلسة الخمس ساعات يُحسب بعد أول جلسة تتجاوز {0}", "The share of a 5-hour session is calculated after the first session above {0}", "首个超过 {0} 的会话结束后计算 5 小时会话占比", "La parte de una sesión de 5 h se calcula tras la primera sesión por encima de {0}", "La part d’une session de 5 h est calculée après la première session au-delà de {0}", "Der Anteil einer 5-Stunden-Sitzung wird nach der ersten Sitzung über {0} berechnet" } },
        { "no_five_plan", new[] { "خطتك بدون حد الساعات الخمس، الحد الأسبوعي فقط", "Your plan has no 5-hour limit, weekly limit only", "你的套餐没有 5 小时限制，仅有每周限制", "Tu plan no tiene límite de 5 horas, solo semanal", "Votre forfait n’a pas de limite de 5 heures, seulement hebdomadaire", "Dein Tarif hat kein 5-Stunden-Limit, nur ein Wochenlimit" } },

        // ----- manual entry -----
        { "manual_title", new[] { "إدخال النسبة يدوياً", "Enter percentage manually", "手动输入百分比", "Introducir porcentaje manualmente", "Saisir le pourcentage manuellement", "Prozentwert manuell eingeben" } },
        { "manual_label", new[] { "نسبة الاستهلاك الأسبوعي كما تظهر في صفحة كلود:", "Weekly usage as shown on the Claude usage page:", "Claude 用量页面显示的每周用量：", "Uso semanal según la página de uso de Claude:", "Utilisation hebdomadaire indiquée sur la page de Claude :", "Wochenverbrauch laut Claude-Nutzungsseite:" } },
        { "save", new[] { "حفظ", "Save", "保存", "Guardar", "Enregistrer", "Speichern" } },
        { "cancel", new[] { "إلغاء", "Cancel", "取消", "Cancelar", "Annuler", "Abbrechen" } },

        // ----- settings -----
        { "settings", new[] { "الإعدادات", "Settings", "设置", "Configuración", "Paramètres", "Einstellungen" } },
        { "sec_popup", new[] { "النافذة المنبثقة", "Popup", "弹出窗口", "Ventana emergente", "Fenêtre contextuelle", "Popup" } },
        { "five_sub", new[] { "شريط حصة الجلسة الحالية تحت أرقام الأسبوع", "Bar for the current session below the weekly numbers", "在每周数字下方显示当前会话的进度条", "Barra de la sesión actual bajo las cifras semanales", "Barre de la session en cours sous les chiffres de la semaine", "Balken der aktuellen Sitzung unter den Wochenwerten" } },
        { "sessleft", new[] { "الجلسات الكاملة الباقية", "Full sessions left", "剩余完整会话", "Sesiones completas restantes", "Sessions complètes restantes", "Verbleibende volle Sitzungen" } },
        { "sessleft_sub", new[] { "كم جلسة خمس ساعات كاملة تبقى لك حتى التجديد", "How many full 5-hour sessions remain until the reset", "重置前还剩多少个完整的 5 小时会话", "Cuántas sesiones completas de 5 horas quedan hasta el reinicio", "Combien de sessions complètes de 5 heures restent avant la réinitialisation", "Wie viele volle 5-Stunden-Sitzungen bis zum Reset bleiben" } },
        { "opus_sub", new[] { "شريط منفصل له إن أرسله الخادم", "Separate bar, if the server reports it", "若服务器提供则单独显示", "Barra aparte, si el servidor la envía", "Barre séparée, si le serveur la fournit", "Eigener Balken, falls der Server ihn meldet" } },
        { "sec_sessions", new[] { "الجلسات والسجل", "Sessions & history", "会话与历史", "Sesiones e historial", "Sessions et historique", "Sitzungen & Verlauf" } },
        { "longs", new[] { "سجل أطول للجلسات (أسابيع)", "Longer session history (weeks)", "更长的会话历史（周）", "Historial de sesiones más largo (semanas)", "Historique des sessions plus long (semaines)", "Längerer Sitzungsverlauf (Wochen)" } },
        { "longs_sub", new[] { "مدة حفظ الجلسات وحساب المتوسط منها، وعند الإيقاف 3 أيام فقط", "How long sessions are kept for the average; off = 3 days only", "会话保留并计算平均值的时长；关闭时仅 3 天", "Tiempo que se guardan las sesiones para el promedio; desactivado = solo 3 días", "Durée de conservation des sessions pour la moyenne ; désactivé = 3 jours", "Wie lange Sitzungen für den Durchschnitt gespeichert werden; aus = nur 3 Tage" } },
        { "minfive", new[] { "أقل حصة تُعدّ جلسة (٪)", "Minimum to count as a session (%)", "计为会话的最低用量（%）", "Mínimo para contar como sesión (%)", "Minimum pour compter comme session (%)", "Mindestwert für eine Sitzung (%)" } },
        { "minfive_sub", new[] { "الجلسات الأصغر تعطي نتيجة غير دقيقة", "Smaller sessions give inaccurate results", "较小的会话结果不准确", "Las sesiones más pequeñas dan resultados imprecisos", "Les sessions plus petites donnent des résultats imprécis", "Kleinere Sitzungen liefern ungenaue Ergebnisse" } },
        { "weekstab", new[] { "تبويب الأسابيع السابقة", "Past weeks tab", "“往周”选项卡", "Pestaña de semanas anteriores", "Onglet des semaines passées", "Tab „Vergangene Wochen“" } },
        { "weekstab_sub", new[] { "في نافذة سجل الاستهلاك", "In the usage history window", "位于用量历史窗口中", "En la ventana del historial de uso", "Dans la fenêtre d’historique", "Im Fenster „Verbrauchsverlauf“" } },
        { "export", new[] { "تصدير السجل إلى إكسل", "Export history to Excel", "导出历史到 Excel", "Exportar historial a Excel", "Exporter l’historique vers Excel", "Verlauf nach Excel exportieren" } },
        { "export_sub", new[] { "إظهار الأمر في قائمة الأيقونة", "Show the command in the icon menu", "在图标菜单中显示此命令", "Mostrar el comando en el menú del icono", "Afficher la commande dans le menu de l’icône", "Befehl im Symbolmenü anzeigen" } },
        { "sec_icon", new[] { "الأيقونة", "Icon", "图标", "Icono", "Icône", "Symbol" } },
        { "preview", new[] { "معاينة", "Preview", "预览", "Vista previa", "Aperçu", "Vorschau" } },
        { "preview_sub", new[] { "كما ستظهر على شريط المهام بالقيم الحالية", "As it will look on the taskbar with current values", "以当前数值在任务栏上的样子", "Cómo se verá en la barra de tareas con los valores actuales", "Tel qu’il apparaîtra dans la barre des tâches", "So erscheint es in der Taskleiste mit den aktuellen Werten" } },
        { "num", new[] { "رقم الفرق داخل الأيقونة", "Difference number in the icon", "图标内显示差值", "Número de diferencia en el icono", "Écart affiché dans l’icône", "Differenz als Zahl im Symbol" } },
        { "num_sub", new[] { "عند الإيقاف تظهر حلقة تمتلئ مع الاستهلاك، والنقطة البيضاء موضع الوقت", "When off, a ring fills with usage and the white dot marks the time", "关闭时显示随用量填充的圆环，白点表示时间位置", "Si está desactivado, un anillo se llena con el uso y el punto blanco marca el tiempo", "Désactivé : un anneau se remplit avec l’utilisation, le point blanc indique le temps", "Aus: Ein Ring füllt sich mit dem Verbrauch, der weiße Punkt zeigt die Zeit" } },
        { "bars", new[] { "شريط صغير تحت الرقم", "Small bar under the number", "数字下方的小进度条", "Barra pequeña bajo el número", "Petite barre sous le nombre", "Kleiner Balken unter der Zahl" } },
        { "bars_sub", new[] { "يمتلئ مع الاستهلاك، والعلامة موضع الوقت", "Fills with usage; the mark shows the time", "随用量填充，标记表示时间位置", "Se llena con el uso; la marca indica el tiempo", "Se remplit avec l’utilisation ; le repère indique le temps", "Füllt sich mit dem Verbrauch; die Markierung zeigt die Zeit" } },
        { "yellow", new[] { "أصفر عند التقدّم بأكثر من (٪)", "Yellow when ahead by more than (%)", "超前超过（%）时显示黄色", "Amarillo si va adelantado más de (%)", "Jaune si en avance de plus de (%)", "Gelb ab Vorsprung von mehr als (%)" } },
        { "yellow_sub", new[] { "الفرق بين الاستهلاك والوقت المنقضي من الأسبوع", "Difference between usage and elapsed time of the week", "用量与本周已过时间之差", "Diferencia entre el uso y el tiempo transcurrido de la semana", "Écart entre l’utilisation et le temps écoulé de la semaine", "Differenz zwischen Verbrauch und vergangener Wochenzeit" } },
        { "red", new[] { "أحمر عند التقدّم بأكثر من (٪)", "Red when ahead by more than (%)", "超前超过（%）时显示红色", "Rojo si va adelantado más de (%)", "Rouge si en avance de plus de (%)", "Rot ab Vorsprung von mehr als (%)" } },
        { "blue", new[] { "أزرق عند التأخر بأكثر من (٪)", "Blue when behind by more than (%)", "落后超过（%）时显示蓝色", "Azul si va retrasado más de (%)", "Bleu si en retard de plus de (%)", "Blau ab Rückstand von mehr als (%)" } },
        { "blue_sub", new[] { "يعني أن عندك مجالاً واسعاً", "Means you have plenty of room", "表示还有很大余量", "Significa que tienes mucho margen", "Signifie que vous avez beaucoup de marge", "Bedeutet: viel Spielraum" } },
        { "sec_general", new[] { "عام", "General", "常规", "General", "Général", "Allgemein" } },
        { "autostart", new[] { "التشغيل مع ويندوز", "Start with Windows", "随 Windows 启动", "Iniciar con Windows", "Démarrer avec Windows", "Mit Windows starten" } },
        { "autostart_sub", new[] { "يبدأ البرنامج تلقائياً عند الدخول", "Starts automatically when you sign in", "登录时自动启动", "Se inicia automáticamente al iniciar sesión", "Démarre automatiquement à l’ouverture de session", "Startet automatisch bei der Anmeldung" } },
        { "poll", new[] { "تحديث البيانات كل (دقائق)", "Refresh data every (minutes)", "数据刷新间隔（分钟）", "Actualizar datos cada (minutos)", "Actualiser les données toutes les (minutes)", "Daten aktualisieren alle (Minuten)" } },
        { "poll_sub", new[] { "الطلب لا يستهلك شيئاً من حصتك", "The request does not use any of your quota", "该请求不会消耗你的额度", "La consulta no consume nada de tu cuota", "La requête ne consomme rien de votre quota", "Die Abfrage verbraucht nichts von deinem Kontingent" } },
        { "language", new[] { "اللغة", "Language", "语言", "Idioma", "Langue", "Sprache" } },
        { "language_sub", new[] { "لغة القوائم والنوافذ", "Language of menus and windows", "菜单和窗口的语言", "Idioma de menús y ventanas", "Langue des menus et des fenêtres", "Sprache von Menüs und Fenstern" } },
        { "lang_auto", new[] { "تلقائية (لغة ويندوز)", "Automatic (Windows)", "自动（Windows）", "Automático (Windows)", "Automatique (Windows)", "Automatisch (Windows)" } },

        // ----- tray menu -----
        { "head", new[] { "استهلاك كلود", "Claude usage", "Claude 用量", "Uso de Claude", "Utilisation de Claude", "Claude-Verbrauch" } },
        { "head_data", new[] { "الأسبوع {0}   ·   الوقت {1}", "Week {0}   ·   Time {1}", "本周 {0}   ·   时间 {1}", "Semana {0}   ·   Tiempo {1}", "Semaine {0}   ·   Temps {1}", "Woche {0}   ·   Zeit {1}" } },
        { "head_nodata", new[] { "استهلاك كلود: لا توجد بيانات بعد", "Claude usage: no data yet", "Claude 用量：暂无数据", "Uso de Claude: aún sin datos", "Utilisation de Claude : pas encore de données", "Claude-Verbrauch: noch keine Daten" } },
        { "upd_check", new[] { "التحقق من التحديثات", "Check for updates", "检查更新", "Buscar actualizaciones", "Rechercher des mises à jour", "Nach Updates suchen" } },
        { "upd_install", new[] { "تثبيت الإصدار {0}", "Install version {0}", "安装版本 {0}", "Instalar la versión {0}", "Installer la version {0}", "Version {0} installieren" } },
        { "signin", new[] { "تسجيل الدخول من جديد", "Sign in again", "重新登录", "Iniciar sesión de nuevo", "Se reconnecter", "Neu anmelden" } },
        { "signin_auto", new[] { "فتح تسجيل الدخول تلقائياً", "Open sign-in automatically", "自动打开登录", "Abrir el inicio de sesión automáticamente", "Ouvrir la connexion automatiquement", "Anmeldung automatisch öffnen" } },
        { "signin_auto_sub", new[] { "عند انتهاء الدخول تُفتح نافذة الدخول وحدها، ويبقى عليك الضغط على الموافقة في المتصفح", "When the sign-in expires, the sign-in window opens by itself; you only confirm in the browser", "登录过期时自动打开登录窗口，您只需在浏览器中确认", "Cuando la sesión caduca, la ventana de inicio de sesión se abre sola; solo confirmas en el navegador", "Quand la connexion expire, la fenêtre de connexion s'ouvre seule ; il suffit de confirmer dans le navigateur", "Wenn die Anmeldung abläuft, öffnet sich das Anmeldefenster von selbst; nur noch im Browser bestätigen" } },
        { "signin_missing", new[] { "لم يُعثر على كلود كود للطرفية على هذا الجهاز", "Claude Code (terminal version) was not found on this PC", "此电脑上未找到 Claude Code（终端版）", "No se encontró Claude Code (versión de terminal) en este PC", "Claude Code (version terminal) est introuvable sur ce PC", "Claude Code (Terminal-Version) wurde auf diesem PC nicht gefunden" } },
        { "upd_auto", new[] { "التحديث التلقائي", "Automatic updates", "自动更新", "Actualizaciones automáticas", "Mises à jour automatiques", "Automatische Updates" } },
        { "upd_auto_sub", new[] { "يثبّت الإصدارات الجديدة بنفسه (الإصدار الحالي {0})", "Installs new versions by itself (current version {0})", "自动安装新版本（当前版本 {0}）", "Instala las nuevas versiones solo (versión actual {0})", "Installe seul les nouvelles versions (version actuelle {0})", "Installiert neue Versionen selbst (aktuelle Version {0})" } },
        { "upd_latest", new[] { "لديك أحدث إصدار ({0}).", "You have the latest version ({0}).", "已是最新版本（{0}）。", "Tienes la última versión ({0}).", "Vous avez la dernière version ({0}).", "Sie haben die neueste Version ({0})." } },
        { "upd_ask", new[] { "يتوفر الإصدار {0} (لديك {1}).\n\n{2}\n\nهل تريد تثبيته الآن؟ سيُعاد تشغيل البرنامج.", "Version {0} is available (you have {1}).\n\n{2}\n\nInstall it now? The program will restart.", "版本 {0} 可用（当前 {1}）。\n\n{2}\n\n现在安装吗？程序将重新启动。", "La versión {0} está disponible (tienes {1}).\n\n{2}\n\n¿Instalarla ahora? El programa se reiniciará.", "La version {0} est disponible (vous avez {1}).\n\n{2}\n\nL’installer maintenant ? Le programme redémarrera.", "Version {0} ist verfügbar (Sie haben {1}).\n\n{2}\n\nJetzt installieren? Das Programm startet neu." } },
        { "upd_found", new[] { "يتوفر الإصدار {0}. ثبّته من القائمة.", "Version {0} is available. Install it from the menu.", "版本 {0} 可用。请从菜单安装。", "La versión {0} está disponible. Instálala desde el menú.", "La version {0} est disponible. Installez-la depuis le menu.", "Version {0} ist verfügbar. Über das Menü installieren." } },
        { "upd_done", new[] { "تم التحديث إلى الإصدار {0}.", "Updated to version {0}.", "已更新到版本 {0}。", "Actualizado a la versión {0}.", "Mis à jour vers la version {0}.", "Auf Version {0} aktualisiert." } },
        { "upd_fail", new[] { "تعذّر التحديث: {0}", "Update failed: {0}", "更新失败：{0}", "Error al actualizar: {0}", "Échec de la mise à jour : {0}", "Update fehlgeschlagen: {0}" } },
        { "refresh", new[] { "تحديث الآن", "Refresh now", "立即刷新", "Actualizar ahora", "Actualiser maintenant", "Jetzt aktualisieren" } },
        { "history", new[] { "سجل الاستهلاك", "Usage history", "用量历史", "Historial de uso", "Historique d’utilisation", "Verbrauchsverlauf" } },
        { "open_page", new[] { "فتح صفحة الاستهلاك", "Open usage page", "打开用量页面", "Abrir página de uso", "Ouvrir la page d’utilisation", "Nutzungsseite öffnen" } },
        { "exit", new[] { "إغلاق البرنامج", "Exit", "退出", "Salir", "Quitter", "Beenden" } },
        { "export_title", new[] { "تصدير السجل", "Export history", "导出历史", "Exportar historial", "Exporter l’historique", "Verlauf exportieren" } },
        { "csv_filter", new[] { "ملفات إكسل (*.csv)|*.csv", "Excel files (*.csv)|*.csv", "Excel 文件 (*.csv)|*.csv", "Archivos de Excel (*.csv)|*.csv", "Fichiers Excel (*.csv)|*.csv", "Excel-Dateien (*.csv)|*.csv" } },
        { "saved3", new[] { "تم الحفظ في ثلاثة ملفات:", "Saved to three files:", "已保存到三个文件：", "Guardado en tres archivos:", "Enregistré dans trois fichiers :", "In drei Dateien gespeichert:" } },
        { "save_fail", new[] { "تعذّر الحفظ: ", "Could not save: ", "保存失败：", "No se pudo guardar: ", "Échec de l’enregistrement : ", "Speichern fehlgeschlagen: " } },

        // ----- history window -----
        { "hist_title", new[] { "سجل استهلاك كلود", "Claude usage history", "Claude 用量历史", "Historial de uso de Claude", "Historique d’utilisation de Claude", "Claude-Verbrauchsverlauf" } },
        { "today", new[] { "اليوم", "Today", "今天", "Hoy", "Aujourd’hui", "Heute" } },
        { "yesterday", new[] { "أمس", "Yesterday", "昨天", "Ayer", "Hier", "Gestern" } },
        { "daybefore", new[] { "قبل أمس", "Day before", "前天", "Anteayer", "Avant-hier", "Vorgestern" } },
        { "weeks_tab", new[] { "الأسابيع السابقة", "Past weeks", "往周", "Semanas anteriores", "Semaines passées", "Vergangene Wochen" } },
        { "c_weekend", new[] { "نهاية الأسبوع", "Week ended", "该周结束", "Fin de la semana", "Fin de la semaine", "Ende der Woche" } },
        { "c_used", new[] { "المستهلك", "Used", "已用", "Usado", "Utilisé", "Verbraucht" } },
        { "c_big", new[] { "جلسات كبيرة", "Big sessions", "大会话", "Sesiones grandes", "Grandes sessions", "Große Sitzungen" } },
        { "c_topday", new[] { "أعلى يوم", "Top day", "最高日", "Día más alto", "Jour le plus élevé", "Stärkster Tag" } },
        { "c_topgain", new[] { "استهلاك ذلك اليوم", "That day's usage", "当天用量", "Uso de ese día", "Utilisation ce jour-là", "Verbrauch an dem Tag" } },
        { "no_weeks", new[] { "لا توجد أسابيع منتهية بعد", "No finished weeks yet", "暂无已结束的周", "Aún no hay semanas terminadas", "Aucune semaine terminée", "Noch keine abgeschlossenen Wochen" } },
        { "sess_tab", new[] { "جلسات الخمس ساعات", "5-hour sessions", "5 小时会话", "Sesiones de 5 horas", "Sessions de 5 heures", "5-Stunden-Sitzungen" } },
        { "avg_sum", new[] { "جلسة الخمس ساعات الكاملة تأخذ في المتوسط {0} من الأسبوع، محسوبة من {1}.", "A full 5-hour session takes on average {0} of the week, based on {1}.", "一个完整的 5 小时会话平均占每周的 {0}，基于 {1}。", "Una sesión completa de 5 horas consume de media {0} de la semana, según {1}.", "Une session complète de 5 heures représente en moyenne {0} de la semaine, sur {1}.", "Eine volle 5-Stunden-Sitzung braucht im Schnitt {0} der Woche, berechnet aus {1}." } },
        { "no_sessions", new[] { "لا توجد جلسات محسوبة بعد.", "No counted sessions yet.", "暂无已计入的会话。", "Aún no hay sesiones contadas.", "Aucune session comptée pour l’instant.", "Noch keine gezählten Sitzungen." } },
        { "only_counted", new[] { "تُحسب فقط الجلسات التي تجاوزت فيها حصة الخمس ساعات {0}، لأن الجلسات الصغيرة تعطي نتيجة غير دقيقة.", "Only sessions where the 5-hour limit went above {0} are counted, because small sessions give inaccurate results.", "仅计入 5 小时额度超过 {0} 的会话，因为小会话结果不准确。", "Solo se cuentan las sesiones en las que el límite de 5 horas superó {0}, porque las pequeñas dan resultados imprecisos.", "Seules les sessions où la limite de 5 heures a dépassé {0} sont comptées, car les petites sessions sont imprécises.", "Gezählt werden nur Sitzungen, in denen das 5-Stunden-Limit {0} überschritten hat, weil kleine Sitzungen ungenau sind." } },
        { "c_start", new[] { "البداية", "Start", "开始", "Inicio", "Début", "Beginn" } },
        { "c_last", new[] { "آخر قراءة", "Last reading", "最后读数", "Última lectura", "Dernière lecture", "Letzte Messung" } },
        { "c_weekinc", new[] { "زيادة الأسبوع", "Weekly increase", "每周增量", "Aumento semanal", "Hausse hebdo", "Wochenanstieg" } },
        { "c_full", new[] { "الجلسة الكاملة من الأسبوع", "Full session of the week", "完整会话占每周", "Sesión completa de la semana", "Session complète de la semaine", "Volle Sitzung der Woche" } },
        { "running", new[] { "جارية الآن", "Running now", "进行中", "En curso", "En cours", "Läuft gerade" } },
        { "late_start", new[] { "الجلسة الحالية بدأت والبرنامج لا يقرأ (أول قراءة كانت {0})، فيُحسب منها فقط ما زاد بعد ذلك: {1}.", "The current session started while the app was not reading (first reading was {0}), so only what was added after that counts: {1}.", "当前会话开始时程序未在读取（首次读数为 {0}），因此只计算之后增加的部分：{1}。", "La sesión actual empezó cuando la app no leía (primera lectura {0}), así que solo cuenta lo añadido después: {1}.", "La session actuelle a commencé quand l'appli ne lisait pas (première lecture {0}) : seul ce qui a été ajouté ensuite compte : {1}.", "Die laufende Sitzung begann, als die App nicht las (erste Messung {0}); gezählt wird nur, was danach dazukam: {1}." } },
        { "five_range", new[] { "‏{0}، من {1} إلى {2}‏", "{0} ({1}→{2})", "{0}（{1}→{2}）", "{0} ({1}→{2})", "{0} ({1}→{2})", "{0} ({1}→{2})" } },
        { "del_session", new[] { "حذف هذه الجلسة", "Delete this session", "删除此会话", "Eliminar esta sesión", "Supprimer cette session", "Diese Sitzung löschen" } },
        { "del_ask", new[] { "حذف الجلسة التي بدأت {0}؟ لن تدخل بعد ذلك في المتوسط، ولا يمكن التراجع.", "Delete the session that started {0}? It will no longer count in the average. This cannot be undone.", "删除 {0} 开始的会话？它将不再计入平均值，且无法撤销。", "¿Eliminar la sesión que empezó el {0}? Ya no contará en la media. No se puede deshacer.", "Supprimer la session commencée le {0} ? Elle ne comptera plus dans la moyenne. Action irréversible.", "Die Sitzung vom {0} löschen? Sie zählt dann nicht mehr im Schnitt. Das lässt sich nicht rückgängig machen." } },
        { "not_reached", new[] { "لم تبلغ {0} بعد", "Not yet {0}", "尚未达到 {0}", "Aún no llega a {0}", "Pas encore {0}", "Noch nicht {0}" } },
        { "c_weekly", new[] { "الأسبوعي", "Weekly", "每周", "Semanal", "Hebdo", "Wöchentlich" } },
        { "c_fiveh", new[] { "الخمس ساعات", "5 hours", "5 小时", "5 horas", "5 heures", "5 Stunden" } },
        { "c_weekchg", new[] { "تغيّر الأسبوعي", "Weekly change", "每周变化", "Cambio semanal", "Variation hebdo", "Wochenänderung" } },
        { "no_readings", new[] { "لا توجد قراءات", "No readings", "暂无读数", "Sin lecturas", "Aucune lecture", "Keine Messwerte" } },
    };
}
