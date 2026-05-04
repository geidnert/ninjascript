#!/usr/bin/env python3
#python3 ff_red_news_scan.py --from 2026-02-23 --to 2026-02-27 --out red_news_times.txt --engine cloudscraper --debug-log ff_debug.txt

"""Scan ForexFactory calendar for high-impact (red-folder) events at target EST times.

Example:
    python3 ff_red_news_scan.py --from 2025-02-01 --to 2025-02-29 --out red_news_times.txt
"""

from __future__ import annotations

import argparse
import random
import re
import sys
import time
from datetime import date, datetime, timedelta
from typing import Iterable

try:
    import requests
except ImportError:  # pragma: no cover
    print("Missing dependency: requests. Install with: pip install requests", file=sys.stderr)
    raise

try:
    from bs4 import BeautifulSoup
except ImportError:  # pragma: no cover
    print("Missing dependency: beautifulsoup4. Install with: pip install beautifulsoup4", file=sys.stderr)
    raise

try:
    from zoneinfo import ZoneInfo
except ImportError:  # pragma: no cover
    print("Python 3.9+ is required (zoneinfo module not found).", file=sys.stderr)
    raise

NY_TZ = ZoneInfo("America/New_York")
TARGET_TIMES = {"08:30", "14:00"}
TARGET_CURRENCY = "USD"
TARGET_IMPACT = "High"
TIMEZONE_RE = re.compile(r"Calendar\s*Time\s*Zone:\s*([^<(\n]+)")
WEEKLY_JSON_URL = "https://nfs.faireconomy.media/ff_calendar_thisweek.json"
COOKIE_FILE_EXAMPLE = "Strategies/ff_cookie.txt"
FORBIDDEN_STOP_LIMIT = 3
MONTHS = {
    "jan": 1,
    "feb": 2,
    "mar": 3,
    "apr": 4,
    "may": 5,
    "jun": 6,
    "jul": 7,
    "aug": 8,
    "sep": 9,
    "oct": 10,
    "nov": 11,
    "dec": 12,
}


def print_cookie_help(reason: str | None = None) -> None:
    if reason:
        print(reason, file=sys.stderr)

    print(
        f"""
How to refresh the ForexFactory cookie file:
  1. Open https://www.forexfactory.com/calendar in your browser.
  2. Let the page fully load. Complete any browser/security check manually.
  3. Open DevTools: Cmd+Option+I on macOS, or F12 on Windows.
  4. Go to Network.
  5. Reload the ForexFactory calendar page.
  6. Click the request named calendar or www.forexfactory.com.
  7. In Headers, scroll to Request Headers.
  8. Copy the full Cookie value from Request Headers.
  9. Save it as one line in {COOKIE_FILE_EXAMPLE}.

Do not use the Response Headers Set-Cookie rows unless you know how to convert
them to a request Cookie header. Do not paste the cookie into chat or logs.

Run the scraper with:
  python3 ff_red_news_scan.py --source html --from YYYY-MM-DD --to YYYY-MM-DD \\
    --out red_news_times.txt --cookie-file {COOKIE_FILE_EXAMPLE}

If ForexFactory still returns 403, use the same browser User-Agent that created
the cookie:
  --user-agent "paste your browser user agent here"
""".strip(),
        file=sys.stderr,
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Find ForexFactory high-impact USD events between two dates and "
            "output rows in format YYYY-MM-DD,HH:MM for ET times 08:30 or 14:00."
        )
    )
    parser.add_argument("--from", dest="start", required=True, help="Start date (YYYY-MM-DD)")
    parser.add_argument("--to", dest="end", required=True, help="End date (YYYY-MM-DD)")
    parser.add_argument("--out", dest="output", default="red_news_times.txt", help="Output txt file path")
    parser.add_argument(
        "--source",
        choices=("weekly-json", "html"),
        default="weekly-json",
        help="Data source to use (default: weekly-json)",
    )
    parser.add_argument(
        "--base-url",
        default="https://www.forexfactory.com",
        help="Calendar host (default: https://www.forexfactory.com)",
    )
    parser.add_argument(
        "--weekly-json-url",
        default=WEEKLY_JSON_URL,
        help=f"Weekly JSON export URL (default: {WEEKLY_JSON_URL})",
    )
    parser.add_argument(
        "--sleep",
        type=float,
        default=0.0,
        help="Fixed delay in seconds between requests (default: 0.0)",
    )
    parser.add_argument(
        "--sleep-min",
        type=float,
        default=1.0,
        help="Minimum random delay in seconds between requests (default: 1.0)",
    )
    parser.add_argument(
        "--sleep-max",
        type=float,
        default=2.5,
        help="Maximum random delay in seconds between requests (default: 2.5)",
    )
    parser.add_argument(
        "--engine",
        choices=("requests", "cloudscraper"),
        default="requests",
        help="HTTP engine to use. cloudscraper can bypass some bot checks (default: requests)",
    )
    parser.add_argument(
        "--cookie",
        default="",
        help="Raw Cookie header value copied from your browser session",
    )
    parser.add_argument(
        "--cookie-file",
        default="",
        help=f"Path to file containing raw Cookie header value, e.g. {COOKIE_FILE_EXAMPLE}",
    )
    parser.add_argument(
        "--user-agent",
        default=(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
            "AppleWebKit/537.36 (KHTML, like Gecko) "
            "Chrome/122.0.0.0 Safari/537.36"
        ),
        help="HTTP User-Agent header to use for requests",
    )
    parser.add_argument(
        "--debug-log",
        default="",
        help="Optional path to write per-row raw scrape debug output",
    )
    return parser.parse_args()


def normalize_cookie_header(raw_cookie: str) -> str:
    cookie = " ".join(line.strip() for line in raw_cookie.splitlines() if line.strip())
    if cookie.lower().startswith("cookie:"):
        cookie = cookie.split(":", 1)[1].strip()
    return cookie


def is_forbidden_error(exc: requests.RequestException) -> bool:
    response = getattr(exc, "response", None)
    return response is not None and response.status_code == 403


def parse_iso_date(value: str) -> date:
    try:
        return datetime.strptime(value, "%Y-%m-%d").date()
    except ValueError as exc:
        raise ValueError(f"Invalid date '{value}'. Expected YYYY-MM-DD.") from exc


def daterange(start: date, end: date) -> Iterable[date]:
    current = start
    while current <= end:
        yield current
        current += timedelta(days=1)


def ff_day_token(d: date) -> str:
    return f"{d.strftime('%b').lower()}{d.day}.{d.year}"


def extract_page_timezone(html: str) -> ZoneInfo | None:
    match = TIMEZONE_RE.search(html)
    if not match:
        return None

    raw_zone = match.group(1).strip()
    aliases = {
        "ET": "America/New_York",
        "EST": "America/New_York",
        "EDT": "America/New_York",
    }
    zone_name = aliases.get(raw_zone, raw_zone)

    try:
        return ZoneInfo(zone_name)
    except Exception:
        return None


def parse_event_time_24h(raw_time: str) -> str | None:
    text = " ".join(raw_time.split())
    if not text:
        return None

    lower = text.lower()
    if "all day" in lower or "tentative" in lower or "day" in lower:
        return None

    cleaned = lower.replace(" ", "")

    try:
        return datetime.strptime(cleaned, "%I:%M%p").strftime("%H:%M")
    except ValueError:
        return None


def parse_row_date(raw_date: str, fallback: date) -> date | None:
    text = " ".join(raw_date.split()).strip()
    if not text:
        return None

    m = re.search(r"(?i)(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)\s+(\d{1,2})", text)
    if not m:
        return None

    month = MONTHS[m.group(1).lower()]
    day_num = int(m.group(2))

    year = fallback.year
    if fallback.month == 1 and month == 12:
        year -= 1
    elif fallback.month == 12 and month == 1:
        year += 1

    try:
        return date(year, month, day_num)
    except ValueError:
        return None


def scan_day_html(session: requests.Session, base_url: str, d: date) -> tuple[set[tuple[str, str]], list[str]]:
    url = f"{base_url.rstrip('/')}/calendar"
    params = {"day": ff_day_token(d)}
    resp = session.get(url, params=params, timeout=20)
    resp.raise_for_status()

    html = resp.text
    soup = BeautifulSoup(html, "html.parser")
    source_tz = extract_page_timezone(html) or NY_TZ

    results: set[tuple[str, str]] = set()
    debug_rows: list[str] = []
    last_time_24: str | None = None
    current_row_date = d

    rows = soup.select("tr.calendar__row, tr.calendar_row")
    debug_rows.append(
        f"REQUEST day={d.isoformat()} url={resp.url} tz={getattr(source_tz, 'key', str(source_tz))} rows={len(rows)}"
    )

    for row in rows:
        date_cell = row.select_one("td.calendar__date, td.date")
        if date_cell is not None:
            parsed_row_date = parse_row_date(date_cell.get_text(" ", strip=True), d)
            if parsed_row_date is not None:
                current_row_date = parsed_row_date

        time_cell = row.select_one("td.calendar__time, td.time")
        raw_time = ""
        if time_cell is not None:
            raw_time = time_cell.get_text(" ", strip=True)
            parsed = parse_event_time_24h(raw_time)
            if parsed:
                last_time_24 = parsed

        is_red = row.select_one(".icon--ff-impact-red") is not None
        if not is_red:
            continue

        currency = ""
        currency_cell = row.select_one("td.calendar__currency, td.currency")
        if currency_cell is not None:
            currency = currency_cell.get_text(" ", strip=True)

        event_name = ""
        event_cell = row.select_one("td.calendar__event, td.event")
        if event_cell is not None:
            event_name = event_cell.get_text(" ", strip=True)

        if not last_time_24:
            debug_rows.append(
                f"RED row_date={current_row_date.isoformat()} raw_time='{raw_time}' parsed_time=None currency='{currency}' event='{event_name}' match=False reason=no_time"
            )
            continue

        src_dt = datetime.combine(current_row_date, datetime.strptime(last_time_24, "%H:%M").time(), tzinfo=source_tz)
        ny_dt = src_dt.astimezone(NY_TZ)
        ny_day = ny_dt.strftime("%Y-%m-%d")
        ny_time = ny_dt.strftime("%H:%M")
        is_match = current_row_date == d and ny_time in TARGET_TIMES and currency == TARGET_CURRENCY

        debug_rows.append(
            "RED "
            f"row_date={current_row_date.isoformat()} raw_time='{raw_time}' parsed_time={last_time_24} "
            f"ny_date={ny_day} ny_time={ny_time} currency='{currency}' event='{event_name}' "
            f"target_currency={TARGET_CURRENCY} target_impact={TARGET_IMPACT} match={is_match}"
        )

        if is_match:
            results.add((ny_day, ny_time))

    return results, debug_rows


def scan_weekly_json(
    session: requests.Session,
    weekly_json_url: str,
    start_date: date,
    end_date: date,
) -> tuple[set[tuple[str, str]], list[str]]:
    resp = session.get(weekly_json_url, timeout=20)
    resp.raise_for_status()

    try:
        rows = resp.json()
    except ValueError as exc:
        raise requests.RequestException(f"Could not parse weekly JSON export: {exc}") from exc

    if not isinstance(rows, list):
        raise requests.RequestException("Weekly JSON export did not return a JSON array.")

    results: set[tuple[str, str]] = set()
    debug_rows: list[str] = [f"REQUEST url={resp.url} rows={len(rows)}"]

    for row in rows:
        if not isinstance(row, dict):
            continue

        raw_date = str(row.get("date", "")).strip()
        currency = str(row.get("country", "")).strip()
        impact = str(row.get("impact", "")).strip()
        event_name = str(row.get("title", "")).strip()

        if not raw_date:
            debug_rows.append(
                f"ROW raw_date='' currency='{currency}' impact='{impact}' event='{event_name}' match=False reason=no_date"
            )
            continue

        try:
            source_dt = datetime.fromisoformat(raw_date)
        except ValueError:
            debug_rows.append(
                f"ROW raw_date='{raw_date}' currency='{currency}' impact='{impact}' event='{event_name}' match=False reason=bad_date"
            )
            continue

        if source_dt.tzinfo is None:
            source_dt = source_dt.replace(tzinfo=NY_TZ)

        ny_dt = source_dt.astimezone(NY_TZ)
        ny_day = ny_dt.strftime("%Y-%m-%d")
        ny_time = ny_dt.strftime("%H:%M")
        is_match = (
            start_date <= ny_dt.date() <= end_date
            and ny_time in TARGET_TIMES
            and currency == TARGET_CURRENCY
            and impact == TARGET_IMPACT
        )

        debug_rows.append(
            "ROW "
            f"raw_date='{raw_date}' ny_date={ny_day} ny_time={ny_time} "
            f"currency='{currency}' impact='{impact}' event='{event_name}' "
            f"target_currency={TARGET_CURRENCY} target_impact={TARGET_IMPACT} match={is_match}"
        )

        if is_match:
            results.add((ny_day, ny_time))

    return results, debug_rows


def main() -> int:
    args = parse_args()

    try:
        start_date = parse_iso_date(args.start)
        end_date = parse_iso_date(args.end)
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        return 2

    if start_date > end_date:
        print("--from date must be on or before --to date.", file=sys.stderr)
        return 2
    if args.sleep_min < 0 or args.sleep_max < 0:
        print("--sleep-min and --sleep-max must be >= 0.", file=sys.stderr)
        return 2
    if args.sleep_min > args.sleep_max:
        print("--sleep-min must be <= --sleep-max.", file=sys.stderr)
        return 2

    if args.engine == "cloudscraper":
        try:
            import cloudscraper  # type: ignore
        except ImportError:
            print(
                "Missing dependency: cloudscraper. Install with: pip install cloudscraper",
                file=sys.stderr,
            )
            return 2
        session = cloudscraper.create_scraper()
    else:
        session = requests.Session()

    session.headers.update(
        {
            "User-Agent": args.user_agent,
            "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
            "Accept-Language": "en-US,en;q=0.9",
            "Referer": f"{args.base_url.rstrip('/')}/calendar",
            "Connection": "keep-alive",
            "Upgrade-Insecure-Requests": "1",
        }
    )
    cookie_raw = normalize_cookie_header(args.cookie)
    if args.cookie_file:
        try:
            cookie_raw = normalize_cookie_header(open(args.cookie_file, "r", encoding="utf-8").read())
        except OSError as exc:
            print(f"Could not read --cookie-file: {exc}", file=sys.stderr)
            print_cookie_help()
            return 2
        if not cookie_raw:
            print_cookie_help(f"--cookie-file '{args.cookie_file}' is empty.")
            return 2
    if cookie_raw:
        session.headers["Cookie"] = cookie_raw
    elif args.source == "html":
        print_cookie_help(
            "No cookie was provided for --source html. ForexFactory often returns 403 without a browser cookie."
        )

    matches: set[tuple[str, str]] = set()
    debug_output: list[str] = []

    if args.source == "weekly-json":
        try:
            matches, debug_output = scan_weekly_json(session, args.weekly_json_url, start_date, end_date)
            print(f"[weekly-json] {start_date.isoformat()}..{end_date.isoformat()} -> {len(matches)} match(es)")
        except requests.RequestException as exc:
            print(f"[weekly-json] request failed: {exc}", file=sys.stderr)
            return 1
    else:
        total_days = (end_date - start_date).days + 1
        consecutive_forbidden = 0
        printed_forbidden_help = False

        for idx, d in enumerate(daterange(start_date, end_date), start=1):
            try:
                day_matches, day_debug = scan_day_html(session, args.base_url, d)
                consecutive_forbidden = 0
                matches.update(day_matches)
                debug_output.extend(day_debug)
                debug_output.append("")
                print(f"[{idx}/{total_days}] {d.isoformat()} -> {len(day_matches)} match(es)")
            except requests.RequestException as exc:
                print(f"[{idx}/{total_days}] {d.isoformat()} -> request failed: {exc}", file=sys.stderr)
                debug_output.append(f"REQUEST day={d.isoformat()} failed: {exc}")
                debug_output.append("")
                if is_forbidden_error(exc):
                    consecutive_forbidden += 1
                    if not printed_forbidden_help:
                        print_cookie_help(
                            "ForexFactory returned 403 Forbidden. The cookie is missing, expired, or tied to a different User-Agent."
                        )
                        printed_forbidden_help = True
                    if consecutive_forbidden >= FORBIDDEN_STOP_LIMIT:
                        print(
                            f"Stopping after {consecutive_forbidden} consecutive 403 responses to avoid spamming ForexFactory.",
                            file=sys.stderr,
                        )
                        return 1
                else:
                    consecutive_forbidden = 0

            wait_seconds = args.sleep + random.uniform(args.sleep_min, args.sleep_max)
            if wait_seconds > 0:
                time.sleep(wait_seconds)

    sorted_rows = sorted(matches)

    with open(args.output, "w", encoding="utf-8") as f:
        for day, hhmm in sorted_rows:
            f.write(f"{day},{hhmm}\n")

    print(f"Wrote {len(sorted_rows)} row(s) to {args.output}")
    if args.debug_log:
        with open(args.debug_log, "w", encoding="utf-8") as f:
            f.write("\n".join(debug_output))
        print(f"Wrote debug log to {args.debug_log}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
