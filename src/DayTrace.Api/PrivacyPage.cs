namespace DayTrace.Api;

/// <summary>
/// Privacy Policy page content served at /privacy.
/// </summary>
public static class PrivacyPage
{
    public const string Html = """
        <!DOCTYPE html>
        <html lang="ru">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Политика конфиденциальности — Событник</title>
            <style>
                * { margin: 0; padding: 0; box-sizing: border-box; }
                body {
                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                    line-height: 1.6;
                    color: #1a1a1a;
                    background: #fafafa;
                    padding: 24px 16px;
                    max-width: 720px;
                    margin: 0 auto;
                }
                h1 { font-size: 1.5rem; margin-bottom: 8px; }
                .updated { color: #666; font-size: 0.85rem; margin-bottom: 24px; }
                h2 { font-size: 1.15rem; margin-top: 24px; margin-bottom: 8px; }
                p, li { margin-bottom: 8px; }
                ul { padding-left: 20px; }
                a { color: #2563eb; }
            </style>
        </head>
        <body>
            <h1>Политика конфиденциальности</h1>
            <p class="updated">Последнее обновление: 23 февраля 2026 г.</p>

            <p>Событник (DayTrace) — сервис личных заметок и рефлексии, работающий через Telegram Bot
            и Telegram Mini App. Настоящая политика описывает, какие данные мы собираем,
            как их используем и защищаем.</p>

            <h2>1. Какие данные мы собираем</h2>
            <ul>
                <li><strong>Идентификатор Telegram</strong> — числовой ID вашего аккаунта
                для аутентификации и связи данных с вашим профилем.</li>
                <li><strong>Часовой пояс</strong> — определяется автоматически при открытии
                Mini App для корректного расчёта дат и напоминаний.</li>
                <li><strong>Настройки</strong> — время напоминания, день окончания недели,
                статус напоминаний.</li>
                <li><strong>События и итоги</strong> — текст событий, оценка важности,
                автоматически сформированные итоги за неделю, месяц и год.</li>
            </ul>

            <h2>2. Как мы используем данные</h2>
            <ul>
                <li>Отображение ваших записей в приложении.</li>
                <li>Отправка ежедневных напоминаний в выбранное вами время.</li>
                <li>Формирование итогов за периоды (неделя, месяц, год).</li>
                <li>Поддержка работы сервиса и исправление ошибок.</li>
            </ul>

            <h2>3. Хранение и защита</h2>
            <ul>
                <li>Данные хранятся в защищённой базе данных PostgreSQL.</li>
                <li>Сессии аутентификации используют SHA-256 хеширование токенов.</li>
                <li>Мы не передаём ваши данные третьим лицам.</li>
                <li>Мы не используем данные для рекламы или аналитических профилей.</li>
            </ul>

            <h2>4. Удаление данных</h2>
            <p>Вы можете удалить свои события в течение 7 дней после создания.
            Для полного удаления аккаунта и всех данных свяжитесь с администратором сервиса.</p>

            <h2>5. Файлы cookie</h2>
            <p>Мы не используем файлы cookie. Аутентификация осуществляется через
            токены Telegram и сессионные Bearer-токены.</p>

            <h2>6. Изменения политики</h2>
            <p>Мы можем обновлять эту политику. Актуальная версия всегда доступна
            по этому адресу.</p>

            <h2>7. Контакты</h2>
            <p>По вопросам конфиденциальности обращайтесь через Telegram Bot
            <a href="https://t.me/day_trace_bot">@day_trace_bot</a>.</p>
        </body>
        </html>
        """;
}
