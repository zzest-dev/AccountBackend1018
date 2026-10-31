# Разработка и сабмодуль

## Структура

```text
src/AccountBackend/         C#-сервер
tests/AccountBackend.Tests/ проверки обработчиков
tests/tcp_smoke.py          проверка реального TCP и перезапуска
data/                      каталог и определения машин
docs/                      документация
patcher/                   Git-сабмодуль APKPatcher1018
server.json                стандартный конфиг
```

## Сборка

Нужен .NET 8 SDK или совместимый SDK, способный собрать `net8.0`. При первой сборке восстанавливается Newtonsoft.Json 13.0.3 из NuGet. Сервер и основные C#-проверки не требуют Python.

```sh
dotnet build src/AccountBackend -c Release
dotnet run --project tests/AccountBackend.Tests -c Release -- data
```

Дополнительный TCP smoke-тест использует Python 3.11+ и отдельную временную базу:

```sh
python tests/tcp_smoke.py
```

Он проверяет framing, гостевой вход, каталог, покупку и повтор, нитро, ник, миссию и её повтор, сохранение после рестарта, целостность SQLite и отказ на слишком большой кадр. Он не подключается к рабочей базе и не останавливает сервер на порту 10118.

Публикация Windows:

```sh
dotnet publish src/AccountBackend -c Release -r win-x64 --self-contained true -o dist/windows
```

Запускайте из результата вместе с `server.json` и `data/`. Для переносимого варианта используйте `--self-contained false`; для Linux — `-r linux-x64` и системную SQLite. Запуск самостоятельной Linux-сборки отдельно не подтверждён.

## Как устроен сабмодуль

В основном репозитории хранится ссылка на конкретный коммит отдельного репозитория патчера, а не копия его файлов. `.gitmodules` задаёт URL и путь. Для получения зафиксированной версии:

```sh
git submodule update --init --recursive
```

Изменения патчера публикуются сначала в его репозиторий, затем обновляется ссылка в бэкенде:

```sh
git -C patcher switch main
git -C patcher pull --ff-only
git -C patcher add <изменённые-файлы>
git -C patcher commit -m "Update APK patcher"
git -C patcher push origin main
git add patcher
git commit -m "Update APK patcher submodule"
git push origin main
```

Коммит бэкенда должен ссылаться на уже доступный удалённый коммит патчера. Для закрытых репозиториев доступ требуется к обоим; CI-тесты аккаунтного сервера выполняются без скачивания сабмодуля.

## Клиентская часть

Обычная операция патчинга использует готовые `patcher/payload/classes.dex` и `patcher/payload/armeabi-v7a/libbackend1018.so`. Их SHA-256 проверяются по манифесту. Исходники Java/C++, скрипт пересборки и исходники Dobby находятся в репозитории патчера. После пересборки payload его файлы и `manifest.json` коммитятся вместе.

APK/OBB, базы аккаунтов, гостевые токены, логи, ключи подписи и каталоги `bin`, `obj`, `dist` в Git не включаются. Собственные C#, Python, Java и C++-файлы поставки не содержат служебных комментариев. Тексты лицензий и комментарии стороннего Dobby сохранены.
