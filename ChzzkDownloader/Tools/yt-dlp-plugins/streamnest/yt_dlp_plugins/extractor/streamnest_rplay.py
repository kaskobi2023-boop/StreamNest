"""RPlay (rplay.live) extractor plugin for StreamNest Downloader."""
import re
import urllib.error
import urllib.parse

from yt_dlp.extractor.common import InfoExtractor
from yt_dlp.utils import (
    ExtractorError,
    float_or_none,
    int_or_none,
    str_or_none,
    unified_strdate,
    std_headers,
)


def _is_auth_error(e):
    if not isinstance(e, ExtractorError) or not e.cause:
        return False
    cause = e.cause
    status = getattr(cause, 'status', None) or getattr(cause, 'code', None)
    return status in (401, 403)


def _is_api_host(url):
    if not url:
        return False
    try:
        parsed = urllib.parse.urlsplit(url)
        if parsed.scheme != 'https':
            return False
        if (parsed.hostname or '').lower() != 'api.rplay.live':
            return False
        if parsed.port not in (None, 443):
            return False
        if parsed.username is not None or parsed.password is not None:
            return False
        return True
    except Exception:
        return False


from yt_dlp.cookies import YoutubeDLCookieJar
import http.cookiejar


class _IsolatedEmptyCookieJar(YoutubeDLCookieJar):
    """Immutable, empty YoutubeDLCookieJar conforming to yt-dlp's RequestHandler
    extension type contract while refusing any cookie storage or retrieval."""

    def __init__(self):
        super().__init__()

        class _RejectAllPolicy(http.cookiejar.DefaultCookiePolicy):
            def set_ok(self, cookie, request):
                return False

            def return_ok(self, cookie, request):
                return False

        self.set_policy(_RejectAllPolicy())

    def set_cookie(self, cookie):
        pass

    def extract_cookies(self, response, request):
        pass

    def add_cookie_header(self, request):
        pass


class _RPlayExactOriginCookiePolicy(http.cookiejar.DefaultCookiePolicy):
    """Cookie policy enforcing exact origin scoping (https://api.rplay.live:443)
    for all RPlay cookies across initial requests, redirects, and Set-Cookie responses."""

    def return_ok(self, cookie, request):
        import time
        if not hasattr(self, '_now') or self._now is None:
            self._now = int(time.time())
        req_url = getattr(request, 'get_full_url', None)
        if callable(req_url):
            url_str = req_url()
        else:
            url_str = getattr(request, 'full_url', '') or getattr(request, 'url', '')

        domain = getattr(cookie, 'domain', '') or ''
        if 'rplay.live' in domain.lower():
            if not _is_api_host(url_str):
                return False
        return super().return_ok(cookie, request)

    def set_ok(self, cookie, request):
        req_url = getattr(request, 'get_full_url', None)
        if callable(req_url):
            url_str = req_url()
        else:
            url_str = getattr(request, 'full_url', '') or getattr(request, 'url', '')

        domain = getattr(cookie, 'domain', '') or ''
        if 'rplay.live' in domain.lower():
            if not _is_api_host(url_str):
                return False
            if domain.startswith('.') or domain.lower() != 'api.rplay.live':
                return False
        return super().set_ok(cookie, request)


class StreamNestRPlayIE(InfoExtractor):
    IE_NAME = 'rplay:streamnest'
    _VALID_URL = r'https?://(?:www\.)?rplay\.live/play/(?P<id>[a-zA-Z0-9_-]+)'
    _API_BASE = 'https://api.rplay.live'

    def _setup_request_scoping(self, auth_token):
        empty_cookiejar = _IsolatedEmptyCookieJar()

        # Sanitize any wildcard or foreign auth cookies currently in downloader.cookiejar
        cj = getattr(self._downloader, 'cookiejar', None)
        token_names = {'_authorization_', 'authorization', 'token', 'access_token'}
        if cj is not None:
            for cookie in list(cj):
                domain = (cookie.domain or '').lower()
                if 'rplay.live' in domain:
                    cj.clear(cookie.domain, cookie.path, cookie.name)
                    if cookie.name.lower() in token_names:
                        cookie.domain = 'api.rplay.live'
                        cookie.domain_specified = True
                        cookie.domain_initial_dot = False
                        cj.set_cookie(cookie)
            cj.set_policy(_RPlayExactOriginCookiePolicy())

        rd = getattr(self._downloader, '_request_director', None)
        if rd:
            orig_send = getattr(rd, '_streamnest_orig_send', None)
            if orig_send is None:
                orig_send = rd.send
                rd._streamnest_orig_send = orig_send

            def scoped_send(request):
                req_url = getattr(request, 'url', None) or getattr(request, 'full_url', '')
                if _is_api_host(req_url):
                    if auth_token and hasattr(request, 'headers'):
                        request.headers['Authorization'] = auth_token
                else:
                    if hasattr(request, 'headers'):
                        request.headers.pop('authorization', None)
                        request.headers.pop('Authorization', None)
                        request.headers.pop('cookie', None)
                        request.headers.pop('Cookie', None)
                    if hasattr(request, 'extensions'):
                        request.extensions['cookiejar'] = empty_cookiejar
                target_send = getattr(rd, '_streamnest_orig_send', orig_send)
                return target_send(request)

            rd.send = scoped_send

        # Urllib redirect hook
        try:
            from yt_dlp.networking._urllib import RedirectHandler
            orig_redirect = getattr(RedirectHandler, '_streamnest_orig_redirect', None)
            if orig_redirect is None:
                orig_redirect = RedirectHandler.redirect_request
                RedirectHandler._streamnest_orig_redirect = orig_redirect

            def scoped_redirect(handler_self, req, fp, code, msg, headers, newurl):
                new_req = orig_redirect(handler_self, req, fp, code, msg, headers, newurl)
                if new_req and not _is_api_host(newurl):
                    if hasattr(new_req, 'headers'):
                        for k in list(new_req.headers.keys()):
                            if k.lower() in ('authorization', 'cookie'):
                                del new_req.headers[k]
                    if hasattr(new_req, 'unredirected_hdrs'):
                        for k in list(new_req.unredirected_hdrs.keys()):
                            if k.lower() in ('authorization', 'cookie'):
                                del new_req.unredirected_hdrs[k]
                return new_req

            RedirectHandler.redirect_request = scoped_redirect
        except Exception:
            pass

        # Requests redirect hook
        try:
            from yt_dlp.networking._requests import RequestsSession
            orig_rebuild_auth = getattr(RequestsSession, '_streamnest_orig_rebuild_auth', None)
            if orig_rebuild_auth is None:
                orig_rebuild_auth = RequestsSession.rebuild_auth
                RequestsSession._streamnest_orig_rebuild_auth = orig_rebuild_auth

            def scoped_rebuild_auth(session_self, prepared_request, response):
                res = orig_rebuild_auth(session_self, prepared_request, response)
                if not _is_api_host(prepared_request.url):
                    for k in list(prepared_request.headers.keys()):
                        if k.lower() in ('authorization', 'cookie'):
                            del prepared_request.headers[k]
                return res

            RequestsSession.rebuild_auth = scoped_rebuild_auth
        except Exception:
            pass

    def _extract_auth_token(self):
        token_names = {'_authorization_', 'authorization', 'token', 'access_token'}
        for origin in ('https://api.rplay.live', 'https://rplay.live', 'https://www.rplay.live'):
            cookie_jar = self._get_cookies(origin)
            if not cookie_jar:
                continue
            for name, cookie in cookie_jar.items():
                if name.lower() in token_names:
                    val = cookie.value if hasattr(cookie, 'value') else str(cookie)
                    if val and val.strip():
                        return val.strip()
        return None

    def _extract_account_context(self):
        cookies = self._get_cookies('https://api.rplay.live') or {}
        context = {}
        for name, field, pattern in (
                ('streamnest_rplay_requestor', 'requestorOid', r'[a-fA-F0-9]{24}'),
                ('streamnest_rplay_login_type', 'loginType', r'[a-zA-Z0-9_-]{1,32}')):
            cookie = cookies.get(name)
            value = getattr(cookie, 'value', '')
            if isinstance(value, str) and re.fullmatch(pattern, value):
                context[field] = value
        return context if len(context) == 2 else {}

    def _real_extract(self, url):
        video_id = self._match_id(url)
        account_context = self._extract_account_context()
        def api_url(path, **query):
            return f'{self._API_BASE}{path}?' + urllib.parse.urlencode({
                **query, **account_context, 'platformType': 'rplay', 'lang': 'ko'})
        content_url = api_url('/content', contentOid=video_id, status='published',
                              withComments='true', withContentMetadata='false',
                              requestCanView='true', includeWatchHistory='true')
        headers = {
            'User-Agent': std_headers['User-Agent'],
            'Referer': f'https://rplay.live/play/{video_id}',
            'Origin': 'https://rplay.live',
            'Accept': 'application/json, text/plain, */*',
            'platform-type': 'rplay',
        }

        user_agent = (self._get_cookies('https://api.rplay.live') or {}).get('streamnest_rplay_user_agent')
        user_agent = getattr(user_agent, 'value', '')
        if isinstance(user_agent, str) and 0 < len(user_agent) <= 512 and not any(ord(c) < 32 or ord(c) == 127 for c in user_agent):
            headers['User-Agent'] = user_agent

        auth_token = self._extract_auth_token()
        self._setup_request_scoping(auth_token)
        api_headers = headers.copy()
        if auth_token:
            api_headers['Authorization'] = auth_token

        auth_error = False
        content_info = None

        # 1. Fetch content metadata
        try:
            content_info = self._download_json(
                content_url, video_id,
                headers=api_headers,
                note='Downloading content metadata',
                errnote='Failed to download content metadata',
                fatal=True)
        except ExtractorError as e:
            if _is_auth_error(e):
                auth_error = True
            content_info = None
        except Exception:
            content_info = None

        if isinstance(content_info, dict) and (content_info.get('statusCode') in (401, 403) or content_info.get('status') in (401, 403)):
            auth_error = True
            content_info = None

        if auth_error:
            self.raise_login_required(
                '웹 영상 로그인 세션을 확인하지 못했거나 구매하지 않은 콘텐츠입니다. 해당 사이트에 로그인한 뒤 다시 시도해주세요.',
                method='cookies')

        if not content_info or not isinstance(content_info, dict):
            # Fallback to webpage if metadata endpoint did not return JSON and was not an auth error
            try:
                webpage = self._download_webpage(
                    f'https://rplay.live/play/{video_id}', video_id,
                    headers=headers,
                    note='Downloading webpage',
                    errnote='Failed to download webpage')
                ld_json = self._search_json_ld(webpage, video_id, default={})
                title = ld_json.get('title') or self._og_search_title(webpage, default=None)
                duration = ld_json.get('duration')
                thumbnail = ld_json.get('thumbnail') or self._og_search_thumbnail(webpage, default=None)
                description = ld_json.get('description') or self._og_search_description(webpage, default=None)
                content_info = {
                    '_id': video_id,
                    'title': title,
                    'length': duration,
                    'previewFile': thumbnail,
                    'introText': description,
                }
            except Exception:
                raise ExtractorError('영상 콘텐츠 정보를 불러오지 못했습니다.', expected=True)

        title = content_info.get('title') or f'Web content {video_id}'
        if content_info.get('drm') is True or content_info.get('has_drm') is True:
            raise ExtractorError('영상 DRM 보호 스트림은 저장할 수 없습니다.', expected=True)
        description = content_info.get('introText')
        duration = float_or_none(content_info.get('length'))
        thumbnail = content_info.get('previewFile') or f'https://pb3.rplay.live/thumbnail/{video_id}_ko'
        user = content_info.get('user')
        uploader = content_info.get('nickname') or (user.get('nickname') if isinstance(user, dict) else None)
        upload_date = unified_strdate(content_info.get('publishedAt'))
        is_adult = bool(content_info.get('isAdultContent', False))
        age_limit = 18 if is_adult else 0

        # Check streamables and files safely with affirmative audio requirements
        streamables = [s for s in (content_info.get('streamables') or []) if isinstance(s, dict)]
        files = [f for f in (content_info.get('files') or []) if isinstance(f, dict)]

        has_positive_dim = any(
            (int_or_none(s.get('width')) or 0) > 0 or (int_or_none(s.get('height')) or 0) > 0
            for s in streamables
        )

        # Require affirmative audio evidence: all entries must explicitly declare width=0 and height=0
        confirmed_audio_streamables = (
            bool(streamables)
            and not has_positive_dim
            and all(
                s.get('width') is not None and s.get('height') is not None
                and int_or_none(s.get('width')) == 0 and int_or_none(s.get('height')) == 0
                for s in streamables
            )
        )

        audio_files_only = (
            bool(files)
            and all(
                any((f.get('name') or '').lower().endswith(ext) for ext in ('.mp3', '.m4a', '.aac', '.wav', '.flac', '.ogg', '.opus'))
                for f in files
            )
        )

        affirmative_audio = (confirmed_audio_streamables or audio_files_only) and not has_positive_dim
        is_audio_only = affirmative_audio

        formats = []

        # The server's canView URL contains the authorized stream key/token.
        # Never invent /content/hlsstream?contentOid=...; it is not a playback URL.

        # 3. Try direct download endpoint
        dl_api_url = api_url('/content/download', contentOid=video_id)
        try:
            dl_resp = self._download_json(
                dl_api_url, video_id,
                headers=api_headers,
                note='Checking download URL',
                errnote='Download URL not available',
                fatal=True)
            if isinstance(dl_resp, dict):
                if dl_resp.get('statusCode') in (401, 403) or dl_resp.get('status') in (401, 403):
                    auth_error = True
                elif dl_resp.get('url'):
                    dl_url = dl_resp['url']
                    parsed_dl = urllib.parse.urlsplit(dl_url)
                    if parsed_dl.scheme in ('http', 'https'):
                        dl_path_lower = parsed_dl.path.lower()
                        detected_ext = None
                        for ext_candidate in ('.m4a', '.mp3', '.aac', '.wav', '.flac', '.ogg', '.opus', '.mp4', '.mov', '.ts', '.mkv'):
                            if dl_path_lower.endswith(ext_candidate):
                                detected_ext = ext_candidate[1:]
                                break
                        ext = detected_ext or ('mp4' if not is_audio_only else 'm4a')
                        is_audio_ext = ext in ('m4a', 'mp3', 'aac', 'wav', 'flac', 'ogg', 'opus')
                        known_acodec = {'mp3': 'mp3', 'flac': 'flac', 'wav': 'wav', 'opus': 'opus', 'ogg': 'vorbis', 'm4a': 'aac', 'aac': 'aac'}.get(ext, 'unknown')
                        dl_headers = headers.copy()
                        dl_headers.pop('authorization', None)
                        dl_headers.pop('Authorization', None)
                        formats.append({
                            'url': dl_url,
                            'format_id': 'direct-download',
                            'ext': ext,
                            'vcodec': 'none' if is_audio_ext or is_audio_only else 'unknown',
                            'acodec': known_acodec,
                            'quality': 10,
                            'http_headers': dl_headers,
                        })
        except ExtractorError as e:
            if _is_auth_error(e):
                auth_error = True
        except Exception:
            pass

        # 4. Try canView URL
        can_view = content_info.get('canView')
        if isinstance(can_view, dict):
            if can_view.get('canView') is False:
                auth_error = True
            can_view_url = can_view.get('url')
            if can_view_url:
                parsed_view = urllib.parse.urlsplit(can_view_url)
                if parsed_view.scheme in ('http', 'https'):
                    path_lower = parsed_view.path.lower()
                    if '.m3u8' in path_lower or (_is_api_host(can_view_url) and path_lower == '/content/hlsstream'):
                        if 'sample_aes' in can_view_url or 'sample_aes' in path_lower:
                            self.report_warning('Content stream uses Apple FairPlay SAMPLE-AES; checking alternative web streams')
                        else:
                            can_view_headers = api_headers if _is_api_host(can_view_url) else headers
                            try:
                                m3u8_fmts = self._extract_m3u8_formats(
                                    can_view_url, video_id,
                                    ext='m4a' if is_audio_only else 'mp4',
                                    entry_protocol='m3u8_native',
                                    m3u8_id='canview-hls',
                                    fatal=True,
                                    headers=can_view_headers)
                                if m3u8_fmts:
                                    formats.extend(m3u8_fmts)
                            except ExtractorError as e:
                                if _is_auth_error(e):
                                    auth_error = True
                            except Exception:
                                pass
                    else:
                        detected_ext = None
                        for ext_candidate in ('.mp4', '.mov', '.m4a', '.aac', '.mp3', '.wav', '.flac', '.ogg', '.opus', '.ts', '.mkv'):
                            if path_lower.endswith(ext_candidate):
                                detected_ext = ext_candidate[1:]
                                break
                        if detected_ext:
                            ext = detected_ext
                            is_audio_ext = ext in ('m4a', 'mp3', 'aac', 'wav', 'flac', 'ogg', 'opus')
                            known_acodec = {'mp3': 'mp3', 'flac': 'flac', 'wav': 'wav', 'opus': 'opus', 'ogg': 'vorbis', 'm4a': 'aac', 'aac': 'aac'}.get(ext, 'unknown')
                            can_view_fmt_headers = headers.copy()
                            can_view_fmt_headers.pop('authorization', None)
                            can_view_fmt_headers.pop('Authorization', None)
                            formats.append({
                                'url': can_view_url,
                                'format_id': 'canview-direct',
                                'ext': ext,
                                'vcodec': 'none' if is_audio_ext or is_audio_only else 'unknown',
                                'acodec': known_acodec,
                                'quality': 5,
                                'http_headers': can_view_fmt_headers,
                            })

        # Validate formats against contradictory video evidence:
        # If affirmative_audio was established by streamables metadata (e.g. width=0, height=0),
        # container extension (like mp4/ts from HLS) must not defeat the audio classification
        # unless there is ACTUAL video evidence (explicit video codec or non-zero dimensions).
        if affirmative_audio:
            has_video_evidence = any(
                (f.get('vcodec') not in (None, 'none', 'unknown') or
                 (int_or_none(f.get('height')) or 0) > 0 or
                 (int_or_none(f.get('width')) or 0) > 0)
                for f in formats
            )
        else:
            has_video_evidence = any(
                (f.get('vcodec') not in (None, 'none', 'unknown') or
                 (int_or_none(f.get('height')) or 0) > 0 or
                 (int_or_none(f.get('width')) or 0) > 0 or
                 f.get('ext') in ('mp4', 'mov', 'ts', 'mkv'))
                for f in formats
            )

        if has_video_evidence:
            is_audio_only = False

        # Ensure all formats have clean http_headers without embedded Authorization.
        # Outgoing requests to https://api.rplay.live are dynamically authorized by _setup_request_scoping,
        # ensuring that downstream segments, fragments, AES keys, and redirects to CDNs never receive Authorization.
        for f in formats:
            f_headers = dict(f.get('http_headers') or headers)
            f_headers.pop('authorization', None)
            f_headers.pop('Authorization', None)
            f['http_headers'] = f_headers

            if is_audio_only:
                # Never overwrite a format with confirmed video
                if (int_or_none(f.get('height')) or 0) > 0 or (f.get('vcodec') and f['vcodec'] not in ('none', 'unknown')):
                    continue
                f['vcodec'] = 'none'
                if not f.get('acodec') or f['acodec'] == 'none':
                    f['acodec'] = 'aac'
                if f.get('ext') in ('mp4', 'ts'):
                    f['ext'] = 'm4a'

        # If still no formats, trigger login requirement or clear error
        if not formats:
            is_paid = bool(content_info.get('isPaidContent', False))
            if not auth_token and (auth_error or is_paid or is_adult):
                self.raise_login_required(
                    '웹 영상 로그인 세션을 확인하지 못했거나 구매하지 않은 콘텐츠입니다. 해당 사이트에 로그인한 뒤 다시 시도해주세요.',
                    method='cookies')
            if auth_error:
                raise ExtractorError('서버가 영상 미디어 요청을 거부했습니다(HTTP 403). 접근 권한과 재생 주소를 확인해주세요.', expected=True)
            raise ExtractorError('해당 사이트에서 재생 가능한 미디어 스트림을 찾지 못했습니다.', expected=True)

        return {
            'id': video_id,
            'title': title,
            'description': description,
            'duration': duration,
            'thumbnail': thumbnail,
            'uploader': uploader,
            'upload_date': upload_date,
            'age_limit': age_limit,
            'formats': formats,
            'http_headers': headers,
        }


__all__ = ['StreamNestRPlayIE']
