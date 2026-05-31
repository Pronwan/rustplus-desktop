$strings = @{
    "" = @{
        "CustomTimerCategory" = "Custom Timer"
        "TimerCreated" = "Timer {0} created for {1}h {2}m"
        "HoverCustomTimer" = "Custom Timer"
        "ActiveTimersCategory" = "Active Timers"
        "AddTimer" = "Add Timer"
        "TimerName" = "Timer Name"
        "TimerCreateCmdLabel" = "Chat Command:"
        "CheckTimerStatusInfo" = "Use chat command below to check status."
        "ChatCmdTimerMaxReached" = "Maximum of 5 timers allowed."
    }
    "de-DE" = @{
        "CustomTimerCategory" = "Eigener Timer"
        "TimerCreated" = "Timer {0} erstellt für {1}h {2}m"
        "HoverCustomTimer" = "Eigener Timer"
        "ActiveTimersCategory" = "Aktive Timer"
        "AddTimer" = "Timer hinzufügen"
        "TimerName" = "Timer Name"
        "TimerCreateCmdLabel" = "Chat-Befehl:"
        "CheckTimerStatusInfo" = "Nutze den Chat-Befehl unten zur Statusabfrage."
        "ChatCmdTimerMaxReached" = "Maximal 5 Timer erlaubt."
    }
    "es-ES" = @{
        "CustomTimerCategory" = "Temporizador"
        "TimerCreated" = "Temporizador {0} creado por {1}h {2}m"
        "HoverCustomTimer" = "Temporizador"
        "ActiveTimersCategory" = "Temporizadores Activos"
        "AddTimer" = "Añadir Temporizador"
        "TimerName" = "Nombre"
        "TimerCreateCmdLabel" = "Comando:"
        "CheckTimerStatusInfo" = "Usa el comando abajo para ver estado."
        "ChatCmdTimerMaxReached" = "Máximo 5 temporizadores permitidos."
    }
    "fr-FR" = @{
        "CustomTimerCategory" = "Minuterie"
        "TimerCreated" = "Minuterie {0} créée pour {1}h {2}m"
        "HoverCustomTimer" = "Minuterie"
        "ActiveTimersCategory" = "Minuteries Actives"
        "AddTimer" = "Ajouter Minuterie"
        "TimerName" = "Nom"
        "TimerCreateCmdLabel" = "Commande:"
        "CheckTimerStatusInfo" = "Utilisez la commande pour voir l'état."
        "ChatCmdTimerMaxReached" = "Maximum 5 minuteries."
    }
    "ru-RU" = @{
        "CustomTimerCategory" = "Таймер"
        "TimerCreated" = "Таймер {0} создан на {1}ч {2}м"
        "HoverCustomTimer" = "Таймер"
        "ActiveTimersCategory" = "Активные таймеры"
        "AddTimer" = "Добавить"
        "TimerName" = "Название"
        "TimerCreateCmdLabel" = "Команда:"
        "CheckTimerStatusInfo" = "Используйте команду для проверки."
        "ChatCmdTimerMaxReached" = "Максимум 5 таймеров."
    }
    "zh-CN" = @{
        "CustomTimerCategory" = "自定义计时器"
        "TimerCreated" = "计时器 {0} 已创建，时长 {1}小时 {2}分钟"
        "HoverCustomTimer" = "自定义计时器"
        "ActiveTimersCategory" = "活动计时器"
        "AddTimer" = "添加计时器"
        "TimerName" = "名称"
        "TimerCreateCmdLabel" = "命令:"
        "CheckTimerStatusInfo" = "使用下方命令查看状态。"
        "ChatCmdTimerMaxReached" = "最多允许 5 个计时器。"
    }
    "ar-SA" = @{
        "CustomTimerCategory" = "مؤقت مخصص"
        "TimerCreated" = "تم إنشاء المؤقت {0} لمدة {1}س {2}د"
        "HoverCustomTimer" = "مؤقت مخصص"
        "ActiveTimersCategory" = "المؤقتات النشطة"
        "AddTimer" = "إضافة مؤقت"
        "TimerName" = "الاسم"
        "TimerCreateCmdLabel" = "أمر:"
        "CheckTimerStatusInfo" = "استخدم الأمر للتحقق."
        "ChatCmdTimerMaxReached" = "الحد الأقصى 5 مؤقتات."
    }
}

function Patch-Resx {
    param([string]$file, [hashtable]$dict)
    
    [xml]$doc = Get-Content $file -Raw
    $root = $doc.DocumentElement
    
    foreach ($key in $dict.Keys) {
        $val = $dict[$key]
        $node = $root.SelectSingleNode("data[@name='$key']")
        if ($node) {
            $node.value = $val
        } else {
            $newEl = $doc.CreateElement("data")
            $newEl.SetAttribute("name", $key)
            $newEl.SetAttribute("xml:space", "preserve")
            
            $valEl = $doc.CreateElement("value")
            $valEl.InnerText = $val
            
            $newEl.AppendChild($valEl) | Out-Null
            $root.AppendChild($newEl) | Out-Null
        }
    }
    
    $doc.Save($file)
    Write-Host "Patched $file"
}

$rootDir = "C:\Users\carst\source\repos\RustPlusDesktop\RustPlusDesktop\RustPlusDesktop\Properties"
Patch-Resx "$rootDir\Resources.resx" $strings[""]

$langDir = "$rootDir\lang"
if (Test-Path $langDir) {
    foreach ($file in Get-ChildItem -Path $langDir -Filter "*.resx") {
        $lang = $file.BaseName.Replace("Resources.", "")
        if ($strings.Contains($lang)) {
            Patch-Resx $file.FullName $strings[$lang]
        } else {
            Patch-Resx $file.FullName $strings[""]
        }
    }
}
