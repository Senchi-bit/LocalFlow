# LocalFlow

Передача файлов в локальной сети без ручной настройки IP. Один компьютер принимает файлы и становится видимым в сети, другой выбирает получателя и отправляет файл.

Обнаружение идёт по mDNS (`_localflow._tcp`), данные — по TCP на порту `45123`. Входящие файлы сохраняются в `Documents/LocalFlow`. Размер одного файла — до 10 ГиБ, целостность проверяется SHA-256.


| Папка                | UI       | Платформа                                      |
| -------------------- | -------- | ---------------------------------------------- |
| `LocalFlow/`         | WPF      | Windows                                        |
| `LocalFlowAvalonia/` | Avalonia | Windows, Linux и другие desktop-платформы .NET |


Оба клиента умеют и отправлять, и принимать. Протокол, порты и логика передачи одинаковые: WPF-клиент на Windows может обмениваться файлами с Avalonia-клиентом на Linux.

## Структура

```
LocalFlowApps/
├── LocalFlow/                 WPF-клиент (Windows)
│   ├── LocalFlow.slnx
│   └── LocalFlow/
└── LocalFlowAvalonia/         Avalonia-клиент (кроссплатформенный)
    ├── LocalFlowAvalonia.slnx
    └── LocalFlowAvalonia/
```

Режим работы выбирается на старте:

- **Отправить** — выбрать файл и получателя из списка устройств в сети
- **Получить** — объявить себя по mDNS и принимать входящие файлы



## Требования

- .NET 10 SDK
- Для WPF: Windows
- Для Avalonia: Windows или Linux (и другие desktop-таргеты .NET)



## Запуск

**WPF (Windows):**

```bash
dotnet run --project LocalFlow/LocalFlow/LocalFlow.csproj
```

**Avalonia:**

```bash
dotnet run --project LocalFlowAvalonia/LocalFlowAvalonia/LocalFlowAvalonia.csproj
```



## Сборка Avalonia под Linux

```bash
dotnet publish LocalFlowAvalonia/LocalFlowAvalonia/LocalFlowAvalonia.csproj `
    -c Release -r linux-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o ./publish/linux-x64
```



## Протокол

TCP-кадры версии 2:

1. отправитель: `Hello` (имя и размер файла)
2. получатель: `HelloAck`
3. поток байтов файла
4. отправитель: `Fin` (SHA-256)
5. получатель: `FinAck` (успех, несовпадение хеша или ошибка)

При отмене отправляется `Abort`. Одновременно допускается до 8 приёмов.