"""Bounded, non-executing Packer/HLS fallback for public general-web videos."""

import hashlib
import ipaddress
import json
import re
import socket
import urllib.parse

from yt_dlp.extractor.generic import GenericIE
from yt_dlp.utils import ExtractorError, encode_base_n, js_to_json


class _PackedHls:
    MAX_PAGE = 2 * 1024 * 1024
    MAX_SCRIPT = 256 * 1024
    MAX_OUTPUT = 1024 * 1024
    MAX_SYMBOLS = 4096
    MAX_BLOCKS = 8
    MAX_DEPTH = 2
    MAX_STREAMS = 10
    STRING = r'''(?:'(?:\\.|[^'\\])*'|"(?:\\.|[^"\\])*")'''
    HEADER = re.compile(r'eval\s*\(\s*function\s*\(\s*p\s*,\s*a\s*,\s*c\s*,\s*k\s*,\s*e\s*,\s*[dr]\s*\)')
    ARGUMENTS = re.compile(
        r'}\s*\(\s*(' + STRING + r')\s*,\s*(\d{1,2})\s*,\s*(\d{1,5})\s*,\s*('
        + STRING + r''')\s*\.\s*split\(\s*['"]\|['"]\s*\)''', re.DOTALL)
    STRINGS = re.compile(STRING, re.DOTALL)
    WORDS = re.compile(r'\b\w+\b', re.ASCII)
    SCRIPTS = re.compile(r'<script\b[^>]*>(.*?)</script\s*>', re.IGNORECASE | re.DOTALL)

    @staticmethod
    def string_value(literal):
        value = json.loads(js_to_json(literal, strict=True))
        if not isinstance(value, str):
            raise ValueError('Expected a string literal')
        return value

    @classmethod
    def unpack(cls, script):
        """Decode Packer data only. Never evaluate the wrapper or decoded code."""
        if len(script) > cls.MAX_SCRIPT:
            return
        header = cls.HEADER.search(script)
        if not header:
            return
        for match in cls.ARGUMENTS.finditer(script, header.end()):
            payload, base, count, symbols = match.groups()
            base, count = int(base), int(count)
            if not 2 <= base <= 62 or not 0 <= count <= cls.MAX_SYMBOLS:
                continue
            try:
                payload = cls.string_value(payload)
                symbols = cls.string_value(symbols).split('|')
            except (ValueError, TypeError):
                continue
            if len(symbols) < count:
                continue
            table = {encode_base_n(i, base): symbols[i] or encode_base_n(i, base) for i in range(count)}
            pieces, size, previous = [], 0, 0
            for word in cls.WORDS.finditer(payload):
                prefix = payload[previous:word.start()]
                replacement = table.get(word[0], word[0])
                size += len(prefix) + len(replacement)
                if size > cls.MAX_OUTPUT:
                    break
                pieces.extend((prefix, replacement))
                previous = word.end()
            else:
                tail = payload[previous:]
                if size + len(tail) <= cls.MAX_OUTPUT:
                    yield ''.join(pieces) + tail

    @classmethod
    def candidates(cls, webpage, page_url):
        if len(webpage) > cls.MAX_PAGE:
            return []
        queue = [(match[1], 0) for match in cls.SCRIPTS.finditer(webpage)
                 if cls.HEADER.search(match[1])][:cls.MAX_BLOCKS]
        result, seen, blocks = [], set(), 0
        while queue and blocks < cls.MAX_BLOCKS:
            script, depth = queue.pop(0)
            for decoded in cls.unpack(script):
                blocks += 1
                for literal in cls.STRINGS.finditer(decoded):
                    try:
                        value = cls.string_value(literal[0])
                        if len(value) > 8192 or any(ord(c) < 32 for c in value):
                            continue
                        url = urllib.parse.urljoin(page_url, value)
                        parsed = urllib.parse.urlsplit(url)
                        if not parsed.path.lower().endswith('.m3u8') or parsed.fragment:
                            continue
                        if url not in seen:
                            seen.add(url)
                            result.append(url)
                            if len(result) >= cls.MAX_STREAMS:
                                return result
                    except (ValueError, TypeError):
                        continue
                if depth + 1 < cls.MAX_DEPTH and cls.HEADER.search(decoded):
                    queue.append((decoded, depth + 1))
                if blocks >= cls.MAX_BLOCKS:
                    break
        return result

    @staticmethod
    def origin(url):
        parsed = urllib.parse.urlsplit(url)
        return f'{parsed.scheme.lower()}://{parsed.netloc.lower()}'

    @classmethod
    def allowed_url(cls, url, page_url):
        try:
            parsed = urllib.parse.urlsplit(url)
            if (parsed.scheme not in ('http', 'https') or not parsed.hostname
                    or parsed.username is not None or parsed.password is not None
                    or any(ord(c) < 33 for c in url) or '\\' in url):
                return False
            # Same-origin resources inherit the entry-page policy enforced by the
            # app. This also lets isolated engine tests use a loopback HTTP server;
            # the app itself still rejects loopback/private/non-HTTPS page inputs.
            if cls.origin(url) == cls.origin(page_url):
                return True
            if parsed.scheme != 'https' or parsed.port not in (None, 443):
                return False
            addresses = socket.getaddrinfo(parsed.hostname, 443, type=socket.SOCK_STREAM)
            return bool(addresses) and all(ipaddress.ip_address(item[4][0]).is_global for item in addresses)
        except (ValueError, OSError):
            return False

    @classmethod
    def headers(cls, page_url, stream_url, include_page_path=False):
        # Do not leak page query tokens to another origin. Never import browser or
        # platform cookies, and never take header values from the decoded script.
        page_url = urllib.parse.urldefrag(page_url)[0]
        origin = cls.origin(page_url)
        cross_origin_referer = urllib.parse.urlunsplit(urllib.parse.urlsplit(page_url)._replace(query='')) if include_page_path else origin + '/'
        return {'Referer': page_url if origin == cls.origin(stream_url) else cross_origin_referer, 'Origin': origin}

    @staticmethod
    def canonical_url(url):
        # Exclude known transport credentials only. Meaningful query selectors
        # such as ?video=1 must still distinguish different videos.
        parsed = urllib.parse.urlsplit(url)
        volatile = {'token', 'access_token', 'auth', 'auth_key', 'expires', 'exp',
                    'signature', 'sig', 'policy', 'key-pair-id', 'hdnea', 'hdnts'}
        query = sorted((key, value) for key, value in urllib.parse.parse_qsl(parsed.query, keep_blank_values=True)
                       if key.lower() not in volatile and not key.lower().startswith(('x-amz-', 'x-goog-')))
        return urllib.parse.urlunsplit((parsed.scheme.lower(), parsed.netloc.lower(), parsed.path,
                                      urllib.parse.urlencode(query), ''))

    @classmethod
    def quality_key(cls, url):
        # Conservative grouping: same URL except an explicit /720p/ or -720p
        # quality component. Never combine arbitrary streams merely by host.
        parsed = urllib.parse.urlsplit(cls.canonical_url(url))
        path = re.sub(r'(?i)(?<=/)(\d{2,4})p(?=/)', '{quality}', parsed.path)
        path = re.sub(r'(?i)([-_])\d{2,4}p(?=[-_.])', r'\1{quality}', path)
        return urllib.parse.urlunsplit(parsed._replace(path=path))

    @classmethod
    def video_id(cls, page_url, stream_url):
        digest = hashlib.sha256(f'{cls.canonical_url(page_url)}\n{cls.quality_key(stream_url)}'.encode()).hexdigest()[:20]
        return f'web-packed-{digest}'


class _StreamNestPackedGenericIE(GenericIE, plugin_name='streamnest_packed'):
    def _extract_embeds(self, url, webpage, *, urlh=None, info_dict=None):
        info_dict = info_dict or {}
        if self._configuration_arg('streamnest_packed', ['false'], ie_key='generic') != ['true']:
            yield from super()._extract_embeds(url, webpage, urlh=urlh, info_dict=info_dict)
            return

        # Keep looking even if generic found a trailer or another embed. Limit
        # collection and report failures without suppressing other candidates.
        generic_entries = []
        try:
            for entry in super()._extract_embeds(url, webpage, urlh=urlh, info_dict=info_dict):
                generic_entries.append(entry)
                if len(generic_entries) >= 20:
                    break
        except ExtractorError:
            self.report_warning('StreamNest: generic candidate failed; continuing bounded packed-HLS discovery')

        page_url = urlh.url if urlh else url
        streams = _PackedHls.candidates(webpage, page_url)
        video_id = info_dict.get('id') or self._generic_id(url)
        groups = []
        manifest_failures = []
        include_path = self._configuration_arg('streamnest_referer', ['origin'], ie_key='generic') == ['page']
        for index, stream_url in enumerate(streams, 1):
            if not _PackedHls.allowed_url(stream_url, page_url):
                self.report_warning('StreamNest: discovered HLS URL rejected by public-network policy')
                continue
            self.to_screen(f'StreamNest: decoding packed HLS candidate {index} without executing JavaScript')
            headers = _PackedHls.headers(page_url, stream_url, include_path)
            try:
                # The normal extract helper overrides fatal=True when the app
                # uses --ignore-no-formats-error. Fetch separately to preserve
                # the actual HTTP failure, without changing global parameters.
                manifest, response = self._download_webpage_handle(
                    stream_url, video_id, note='Downloading packed HLS manifest',
                    errnote='Failed to download packed HLS manifest', headers=headers, fatal=True)
                if not manifest.lstrip('\ufeff').startswith('#EXTM3U'):
                    raise ExtractorError('Response is not an HLS playlist', expected=True)
                formats, subtitles = self._parse_m3u8_formats_and_subtitles(
                    manifest, response.url, 'mp4', m3u8_id='hls', headers=headers,
                    entry_protocol='m3u8_native', video_id=video_id, fatal=True)
            except ExtractorError as error:
                status = getattr(error.cause, 'status', None)
                # Log the server and status, never signed paths, queries, cookies
                # or the raw upstream response. Keep trying other valid videos.
                detail = f'HTTP Error {status}' if isinstance(status, int) else 'manifest request failed'
                manifest_failures.append(detail)
                self.report_warning(f'StreamNest: HLS candidate {index} host={urllib.parse.urlsplit(stream_url).hostname}: {detail}')
                continue
            if not formats:
                manifest_failures.append('manifest contains no readable formats')
                self.report_warning('StreamNest: discovered HLS URL has no readable formats; inspect the manifest request error')
                continue
            hint = re.search(r'(?:/|[-_])(\d{2,4})p(?:/|[-_.])', urllib.parse.urlsplit(stream_url).path, re.IGNORECASE)
            for fmt in formats:
                fmt['http_headers'] = {**fmt.get('http_headers', {}), **headers}
                if hint and not fmt.get('height') and fmt.get('vcodec') != 'none':
                    fmt['height'] = int(hint[1])
                identity = _PackedHls.canonical_url(fmt['url']) + str((fmt.get('vcodec'), fmt.get('acodec'), fmt.get('language')))
                fmt['format_id'] = 'sn-' + hashlib.sha256(identity.encode()).hexdigest()[:12]
            entry = {
                'id': _PackedHls.video_id(page_url, stream_url),
                'title': (info_dict.get('title') or video_id) + (f' (HLS {index})' if len(streams) > 1 else ''),
                'formats': formats, 'subtitles': subtitles, 'http_headers': headers,
            }
            for key in ('thumbnail', 'uploader', 'channel', 'description'):
                if info_dict.get(key):
                    entry[key] = info_dict[key]
            self._extra_manifest_info(entry, stream_url)
            all_urls = {_PackedHls.canonical_url(fmt['url']) for fmt in formats}
            urls = {_PackedHls.canonical_url(fmt['url']) for fmt in formats
                    if fmt.get('vcodec') != 'none' and (fmt.get('height') or fmt.get('vcodec') not in (None, 'unknown'))}
            # Shared audio is not evidence that two video masters are the same
            # content. Keep it in formats, but exclude it from identity matching.
            key = _PackedHls.quality_key(stream_url)
            matches = [group for group in groups if key in group['keys'] or urls & group['urls']]
            if matches:
                target = matches[0]
                for other in matches[1:]:
                    target['entry']['formats'].extend(other['entry']['formats'])
                    target['keys'].update(other['keys'])
                    target['urls'].update(other['urls'])
                    target['all_urls'].update(other['all_urls'])
                    groups.remove(other)
                known = {fmt['format_id'] for fmt in target['entry']['formats']}
                target['entry']['formats'].extend(fmt for fmt in formats if fmt['format_id'] not in known)
                target['keys'].add(key)
                target['urls'].update(urls)
                target['all_urls'].update(all_urls)
                if entry.get('is_live'):
                    target['entry']['is_live'] = True
                self._merge_subtitles(subtitles, target=target['entry']['subtitles'])
            else:
                groups.append({'keys': {key}, 'urls': urls, 'all_urls': all_urls, 'entry': entry})

        if manifest_failures and not groups and not generic_entries:
            denied = any(reason in ('HTTP Error 401', 'HTTP Error 403') for reason in manifest_failures)
            marker = 'HLS_ACCESS_DENIED' if denied else 'HLS_FETCH_FAILED'
            raise ExtractorError(
                f'[StreamNest:{marker}] HLS URLs were found, but no manifest could be read: '
                + '; '.join(dict.fromkeys(manifest_failures)), expected=True)

        packed_urls = set().union(*(group['all_urls'] for group in groups)) if groups else set()
        for index, group in enumerate(groups, 1):
            entry = group['entry']
            entry['id'] = _PackedHls.video_id(page_url, sorted(group['keys'])[0])
            entry['title'] = (info_dict.get('title') or video_id) + (f' (HLS {index})' if len(groups) > 1 else '')
            yield entry
        remaining = 20 - len(groups)
        for entry in generic_entries:
            urls = {_PackedHls.canonical_url(fmt['url']) for fmt in entry.get('formats', []) if fmt.get('url')}
            if entry.get('url'):
                urls.add(_PackedHls.canonical_url(entry['url']))
            if remaining > 0 and (not urls or not urls.issubset(packed_urls)):
                yield entry
                remaining -= 1
