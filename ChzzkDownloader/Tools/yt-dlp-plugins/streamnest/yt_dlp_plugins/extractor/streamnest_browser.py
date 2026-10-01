"""Parse the bounded, ephemeral result of StreamNest's private browser capture."""
import hashlib
import json
import os
import re
import urllib.parse

from yt_dlp.extractor.common import InfoExtractor
from yt_dlp.utils import ExtractorError
from .streamnest_packed import _PackedHls


class StreamNestBrowserIE(InfoExtractor):
    _VALID_URL = r'streamnest-browser:(?P<id>[0-9a-f]{32})$'
    IE_NAME = 'streamnest:browser'

    def _real_extract(self, url):
        capture_id = self._match_id(url)
        path = os.environ.get('STREAMNEST_BROWSER_CAPTURE', '')
        if os.path.basename(path) != f'capture-{capture_id}.json':
            raise ExtractorError('Browser capture input is unavailable', expected=True)
        with open(path, encoding='utf-8') as source:
            raw = source.read(32 * 1024 * 1024 + 1)
        if len(raw) > 32 * 1024 * 1024:
            raise ExtractorError('Browser capture exceeds size limit', expected=True)
        data = json.loads(raw)
        page = data['PageUrl']
        title = data.get('Title') or urllib.parse.urlsplit(page).hostname
        include_path = self._configuration_arg('streamnest_referer', ['origin'], ie_key='generic') == ['page']
        media = {item['Url']: item.get('Manifest') for item in data.get('Media', [])[:20]}
        for candidate in _PackedHls.candidates(data.get('Html', ''), page):
            if len(media) < 20:
                media.setdefault(candidate, None)
        groups = []
        inspected = {}
        for stream, manifest in media.items():
            if not _PackedHls.allowed_url(stream, page):
                continue
            headers = _PackedHls.headers(page, stream, include_path)
            if data.get('UserAgent'):
                headers['User-Agent'] = data['UserAgent']
            direct = urllib.parse.urlsplit(stream).path.lower().endswith('.mp4') and manifest is None
            is_master = False
            duration = None
            try:
                if direct:
                    formats = [{'url': stream, 'ext': 'mp4'}]
                else:
                    if manifest is None:
                        manifest, response = self._download_webpage_handle(stream, capture_id,
                            note='Reading browser-discovered playlist', headers=headers)
                        stream = response.url
                    if not manifest.lstrip('\ufeff').startswith('#EXTM3U') or len(manifest) > 1048576:
                        continue
                    if not self._safe_manifest(manifest, stream, page):
                        continue
                    # SAMPLE-AES/DRM and live-only playlists are not downloadable public VODs.
                    if re.search(r'METHOD=(?!NONE|AES-128)[A-Z0-9-]+', manifest):
                        continue
                    if '#EXTINF:' in manifest and '#EXT-X-ENDLIST' not in manifest:
                        continue
                    formats, _ = self._parse_m3u8_formats_and_subtitles(manifest, stream, 'mp4',
                        entry_protocol='m3u8_native', m3u8_id='browser', headers=headers, video_id=capture_id)
                    is_master = '#EXT-X-STREAM-INF:' in manifest
                    readable = []
                    for fmt in formats[:40]:
                        child_url = fmt['url']
                        if not _PackedHls.allowed_url(child_url, page):
                            continue
                        if child_url not in inspected:
                            child = manifest if child_url == stream else media.get(child_url)
                            if child is None:
                                child = self._download_webpage(child_url, capture_id,
                                    note='Checking selected playlist', headers={**headers,
                                        **_PackedHls.headers(page, child_url, include_path)}, fatal=False)
                            inspected[child_url] = child
                        child = inspected[child_url]
                        if (not child or '#EXT-X-ENDLIST' not in child or not self._safe_manifest(child, child_url, page)
                                or re.search(r'METHOD=(?!NONE|AES-128)[A-Z0-9-]+', child)):
                            continue
                        seconds = sum(float(n) for n in re.findall(r'^#EXTINF:([\d.]+)', child, re.M))
                        if seconds > 0:
                            duration = max(duration or 0, seconds)
                        readable.append(fmt)
                    formats = readable
                formats = [f for f in formats if not f.get('has_drm') and _PackedHls.allowed_url(f['url'], page)]
                if not formats:
                    continue
                for fmt in formats:
                    fmt['http_headers'] = {**headers, **_PackedHls.headers(page, fmt['url'], include_path)}
                    hint = re.search(r'(?:/|[-_])(\d{2,4})p(?:/|[-_.])', urllib.parse.urlsplit(fmt['url']).path, re.I)
                    if hint and not fmt.get('height'):
                        fmt['height'] = int(hint[1])
                    identity = _PackedHls.canonical_url(fmt['url']) + str((fmt.get('vcodec'), fmt.get('acodec'), fmt.get('language')))
                    fmt['format_id'] = 'browser-' + hashlib.sha256(identity.encode()).hexdigest()[:12]
                entry = {'title': title, 'formats': formats, 'http_headers': headers,
                         'thumbnail': data.get('Thumbnail'), 'streamnest_browser': True}
                if duration:
                    entry['duration'] = duration
                key = _PackedHls.quality_key(stream)
                urls = {_PackedHls.canonical_url(f['url']) for f in formats if f.get('vcodec') != 'none'}
                matches = [g for g in groups if key in g['keys'] or urls & g['urls']]
                if matches:
                    target = matches[0]
                    for other in matches[1:]:
                        target['entry']['formats'].extend(other['entry']['formats'])
                        target['keys'].update(other['keys'])
                        target['masters'].update(other['masters'])
                        target['urls'].update(other['urls'])
                        groups.remove(other)
                    # A browser can fetch both master and child. Keep the master format's
                    # resolution/codecs instead of adding a duplicate "unknown quality".
                    combined = target['entry']['formats'] + formats
                    unique = {}
                    for fmt in sorted(combined, key=lambda f: bool(f.get('height')), reverse=True):
                        identity = (_PackedHls.canonical_url(fmt['url']), fmt.get('vcodec') == 'none', fmt.get('language'))
                        unique.setdefault(identity, fmt)
                    target['entry']['formats'] = list(unique.values())
                    target['keys'].add(key)
                    if is_master:
                        target['masters'].add(key)
                    target['urls'].update(urls)
                    if entry.get('duration'):
                        target['entry']['duration'] = max(entry['duration'], target['entry'].get('duration', 0))
                else:
                    groups.append({'keys': {key}, 'masters': {key} if is_master else set(), 'urls': urls, 'entry': entry})
            except ExtractorError:
                self.report_warning('A browser-discovered stream could not be read; continuing other candidates')
        if not groups:
            raise ExtractorError('브라우저에서도 저장 가능한 공개 영상을 찾지 못했습니다. 로그인·DRM 또는 직접 재생이 필요한 페이지일 수 있습니다.', expected=True)
        entries = []
        for group in groups:
            entry = group['entry']
            entry['id'] = 'browser-' + _PackedHls.video_id(page, sorted(group['masters'] or group['keys'])[0])
            entries.append(entry)
        return self.playlist_result(entries, capture_id, title)

    @staticmethod
    def _safe_manifest(manifest, stream, page):
        resources = [line.strip() for line in manifest.splitlines() if line.strip() and not line.startswith('#')]
        resources.extend(re.findall(r'URI="([^"]+)"', manifest))
        # Check each distinct origin once, including relative segment/key references.
        origins = set()
        for item in resources:
            resource = urllib.parse.urljoin(stream, item)
            origin = _PackedHls.origin(resource)
            if origin not in origins:
                if not _PackedHls.allowed_url(resource, page):
                    return False
                origins.add(origin)
        return True
