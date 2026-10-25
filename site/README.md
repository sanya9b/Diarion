# diarion.org — статичний сайт

Усе, що лежить у цій папці, і є сайт: жодного білду, жодного JavaScript, жодних сторонніх
запитів. Шрифти — підмножини Montserrat із застосунку, іконки — з `Resources/AppIcon`,
палітра — з `Resources/Styles/Colors.xaml`.

| URL | Файл | Навіщо |
|---|---|---|
| `/` | `index.html` | лендинг українською |
| `/en/` | `en/index.html` | лендинг англійською |
| `/privacy` | `privacy.html` | політика приватності (UA) — поле App Store Connect для української локалі |
| `/en/privacy` | `en/privacy.html` | політика приватності (EN) — те саме поле для англійської |
| `/support` | `support.html` | URL підтримки — обов'язкове поле подачі, `mailto:` там не приймається |
| `/en/support` | `en/support.html` | підтримка англійською |
| `/terms` | `terms.html` | умови використання й підписки (UA) — посилання з пейволу застосунку (`StoreLinks.Terms`) і з опису в магазині, вимога Apple 3.1.2 |
| `/en/terms` | `en/terms.html` | те саме англійською |
| `/404` | `404.html` | Cloudflare віддає її на будь-який неіснуючий шлях |
| | `_headers` | CSP без скриптів, HSTS, кеш для шрифтів — Cloudflare читає файл сам |
| | `robots.txt`, `sitemap.xml` | індексація з hreflang UA/EN |
| | `og.png`, `og-en.png` | прев'ю для месенджерів і соцмереж |

Cloudflare сам прибирає `.html` з адрес: `privacy.html` віддається як `/privacy`, а `/privacy.html`
редиректить на `/privacy`. Тому в посиланнях усюди чисті шляхи.

## Реквізити в політиці — заповнені

Станом на 2026-09-02 у `privacy.html` та `en/privacy.html` стоять фінальні значення:
розробник **Кугай Вікторія Юріївна / Victoria Kuhai** (фізична особа), чинна з **13.10.2026** (07.09.2026 у першій публікації; 20.09.2026 додано рядок про поля профілю; 13.10.2026 — розділ про підписку, разом із новою сторінкою `terms.html`),
контакт **support@diarion.org** (заведена через Email Routing, крок 3 нижче). Ті самі значення
продубльовані в `store/privacy-policy.md`.

Ім'я має збігатися з іменем в акаунті Apple Developer і в Google Play Console — рецензенти звіряють.
При зміні тексту політики оновлюйте дату в обох мовах одночасно.

## Деплой на Cloudflare

Домен уже на Cloudflare, тож усе робиться в одній панелі.

### 1. Cloudflare Pages

**Варіант А — перетягнути папку (найшвидше, без git).**
Workers & Pages → Create → Pages → *Upload assets* → назва проєкту `diarion` → перетягнути
папку `site` цілком (не її вміст окремо) → Deploy. Отримаєте `diarion.pages.dev`.
Кожне наступне оновлення — знову перетягнути папку.

**Варіант В — воркфлоу `.github/workflows/deploy-site.yml` (чинний з 2026-10-25).** Кожен пуш у
`main`, що зачіпає `site/`, викладає теку Wrangler-ом у наявний проєкт Pages; збірки немає. Потрібно
один раз у GitHub → Settings → Secrets and variables → Actions:
- секрет `CLOUDFLARE_API_TOKEN` — API-токен Cloudflare з правом *Account → Cloudflare Pages → Edit*
  (створюється в Cloudflare: My Profile → API Tokens → Create Token → Custom token);
- секрет `CLOUDFLARE_ACCOUNT_ID` — Workers & Pages → Overview, права колонка *Account ID*;
- змінна (вкладка *Variables*) `CLOUDFLARE_PAGES_PROJECT` — назва проєкту Pages, як у списку
  Workers & Pages.
Токен ніколи не потрапляє в репозиторій — він публічний. Запустити вручну: Actions → Deploy site →
Run workflow. Після заливки воркфлоу сам перевіряє, що `/`, `/terms`, `/en/terms` і `/privacy`
відповідають 200.

**Варіант Б — з GitHub через Cloudflare (оновлюється саме при пуші в `main`).**
Workers & Pages → Create → Pages → *Connect to Git* → репозиторій `sanya9b/Diarion` →
Production branch `main` → Framework preset **None** → Build command *порожньо* →
Build output directory `site` → Save and Deploy. Пуші в інші гілки дають preview-адреси й на
продакшен не впливають.

Репозиторій приватний, і таким має лишатись — сайт більше ніде не заявляє, що код відкритий.
Приватний репозиторій для Pages не перешкода: під час *Connect to Git* GitHub попросить дати
доступ Cloudflare Pages App саме до цього репозиторію, і цього достатньо.

### 2. Домен

У проєкті Pages → Custom domains → Set up a custom domain → `diarion.org` → Activate.
Cloudflare сам створить CNAME, бо зона вже тут. Сертифікат з'являється за кілька хвилин.

`www.diarion.org` додати другим custom domain (щоб з'явився проксований DNS-запис), а потім
зробити редирект на apex, бо сам Pages віддаватиме сайт за обома адресами без редиректу:
домен → Rules → Redirect Rules → Create rule → шаблон **Redirect from WWW to root** → Deploy.

### 3. Пошта на домені (безкоштовно, без поштового сервера)

Cloudflare → домен `diarion.org` → Email → Email Routing → Get started:
- Destination address — ваша особиста скринька, підтвердити листом;
- Custom addresses → Create address → `support` → Action *Send to* → ваша скринька;
- Enable Email Routing — Cloudflare сам додасть MX і SPF-записи.

Після цього листи на `support@diarion.org` приходитимуть вам. Відповідати з цієї адреси
можна через «Send mail as» у Gmail або аналог у вашій пошті.

### 4. Одне налаштування зони, яке інакше зламає сторінку

Сайт віддається з `Content-Security-Policy` без скриптів. Функція Cloudflare **Email Address
Obfuscation** вставляє на сторінку свій скрипт замість кожної пошти, і під CSP він не
виконається — адреса покажеться як `[email protected]`. У HTML пошти вже обгорнуті
`<!--email_off-->`, але надійніше вимкнути функцію зовсім:
домен → Security → Settings (або Scrape Shield) → **Email Address Obfuscation → Off**.
Заодно переконайтеся, що **Rocket Loader** вимкнений (він і так off за замовчуванням).

### 5. Перевірка

```
https://diarion.org/            https://diarion.org/en/
https://diarion.org/privacy     https://diarion.org/en/privacy
https://diarion.org/support     https://diarion.org/en/support
```

- у кожній відкрити DevTools → Network: жодного запиту поза `diarion.org`;
- Console чиста (порушень CSP немає);
- пошта на сторінці підтримки читається, а не `[email protected]`;
- у App Store Connect: Privacy Policy URL для української локалі — `/privacy`, для англійської —
  `/en/privacy`, Support URL — `/support`.

## Оновлення політики

Кожна зміна тексту політики — нова дата в `__EFFECTIVE_DATE__`-полі обох мов і новий деплой.
Джерело правди про те, що застосунок насправді робить, — `store/privacy-policy.md`, де кожен
абзац посилається на файл коду; сюди переносити текст **без** цих посилань.

## Локальний перегляд

```bash
cd site && python -m http.server 8765
# http://127.0.0.1:8765/
```

Відкривати через `file://` не варто: шляхи в HTML абсолютні (`/styles.css`), як на сервері.
