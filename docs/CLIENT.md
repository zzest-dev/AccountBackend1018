# Подключение клиента 10.18

## Требования

Поддержан оригинальный APK **10.18 / versionCode 100118 / armeabi-v7a** с SHA-256:

```text
2941a49eaf332930580b81bb2e8012b6684e6103031551d176a765b4ed152848
```

Нужен соответствующий оригинальный OBB 10.18. Патчер отклонит другую версию, ARM64-вариант или уже изменённый APK. Файлы игры предоставляет сам пользователь.

## Windows: сервер и патчер

1. Запустите C#-сервер. Дождитесь строки `Account backend 10.18 listening on 0.0.0.0:10118; multiplayer disabled`.
2. Найдите IPv4 ПК в `ipconfig`. Телефон должен иметь доступ к этому адресу и TCP-порту 10118. Для подключения из домашней сети используйте один Wi-Fi/LAN; мобильная сеть не имеет доступа к частному адресу `192.168.*.*`.
3. Установите Python 3.11+, JDK и Android SDK Build Tools. Можно использовать SDK/JDK из установленного Android-модуля Unity, указав их пути.
4. Выполните из корня репозитория:

```powershell
$env:JAVA_HOME = 'C:\Android\jdk'
$env:ANDROID_SDK_ROOT = 'C:\Android\sdk'
python patcher/patch.py 'C:\Games\original-10.18.apk' --host 192.168.1.10 --output 'C:\Games\patched-10.18.apk'
```

5. Установите APK. Он имеет отдельный package `com.MadOut.BIG.backend1018` и может стоять рядом с оригинальной игрой. Для обновления уже установленного модифицированного клиента нужен тот же ключ подписи. Новый локальный ключ патчера отличается от ключей старых тестовых сборок; при несовпадении подписи используйте прежний ключ или заранее сохраните данные перед переустановкой.
6. Перенесите OBB без изменения содержимого в путь, указанный ниже.

```text
/sdcard/Android/obb/com.MadOut.BIG.backend1018/main.100118.com.MadOut.BIG.backend1018.obb
```

## ADB

```sh
adb devices
adb install -r patched-10.18.apk
adb shell mkdir -p /sdcard/Android/obb/com.MadOut.BIG.backend1018
adb push main.100118.com.MadOut.BIG.obb /sdcard/Android/obb/com.MadOut.BIG.backend1018/main.100118.com.MadOut.BIG.backend1018.obb
```

После первого запуска адрес можно изменить отдельным текстовым файлом:

```ini
ip=192.168.1.10
port=10118
```

```sh
adb push backend.txt /sdcard/Android/data/com.MadOut.BIG.backend1018/files/backend.txt
adb shell am force-stop com.MadOut.BIG.backend1018
adb shell am start -n com.MadOut.BIG.backend1018/com.bigcitylegacy.backend.BackendActivity
```

При нескольких устройствах добавляйте `-s <serial>` после `adb`. Патчер принимает IPv4; доменные имена и IPv6 в этой версии не поддержаны.

Для эмулятора на том же ПК можно использовать туннель:

```sh
adb reverse tcp:10118 tcp:10118
```

При таком подключении в `backend.txt` ставится `ip=127.0.0.1`. После перезапуска эмулятора туннель может понадобиться создать заново. На обычном телефоне `127.0.0.1` означает сам телефон, если ADB reverse не настроен.

## Если висит Find Mirror

- Проверьте, что используется модифицированный APK, а не оригинальный.
- Посмотрите `backend.txt`: адрес сервера может остаться от предыдущей установки.
- Убедитесь, что сервер запущен, порт не занят другим процессом, TCP 10118 разрешён брандмауэром и телефон доступен из той же сети.
- В `adb logcat` должны появиться `Config: ...` и `Hooks installed for verified ARMv7 10.18`. Ошибка `-3` означает несовпадение ожидаемых методов игры; не переносите адреса хуков на другую версию.

```sh
adb logcat -v threadtime Backend1018:I Unity:I AndroidRuntime:E libc:F '*:S'
```

`Not enough storage space to install required resources` появляется до аккаунтного входа: проверьте свободное место, оригинальный OBB, его путь и права приложения на внешние файлы. Ответы аккаунтного сервера эту ошибку не исправляют. Не удаляйте данные приложения без резервной копии, если нужно сохранить гостевой доступ к аккаунту.

Пустой список мультиплеерных серверов в C#-версии ожидаем: игровой сервер и конвертер Mirror ↔ UNet не реализованы.
