import importlib.util
from pathlib import Path
import unittest
from unittest.mock import MagicMock, patch

from yt_dlp import YoutubeDL
from yt_dlp.utils import ExtractorError

PLUGIN = Path(__file__).resolve().parents[1] / 'ChzzkDownloader/Tools/yt-dlp-plugins/streamnest/yt_dlp_plugins/extractor/streamnest_rplay.py'
spec = importlib.util.spec_from_file_location('rplay_plugin_test', PLUGIN)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class RPlayExtractorTests(unittest.TestCase):
    def test_media_denial_with_collected_session_is_not_reported_as_missing_login(self):
        from types import SimpleNamespace
        content = {'title': 'Synthetic', 'canView': {'canView': False}, 'isPaidContent': True}
        with patch.object(self.ie, '_get_cookies', return_value={'_AUTHORIZATION_': SimpleNamespace(value='synthetic_token')}), \
                patch.object(self.ie, '_download_json', side_effect=[content, None]):
            with self.assertRaises(ExtractorError) as error:
                self.ie._real_extract('https://rplay.live/play/denied')
        self.assertIn('HTTP 403', str(error.exception))
        self.assertNotIn('로그인', str(error.exception))

    def test_declared_drm_is_not_downloaded(self):
        with patch.object(self.ie, '_download_json', return_value={'title': 'Synthetic', 'drm': True}), \
                patch.object(self.ie, '_extract_m3u8_formats') as hls:
            with self.assertRaisesRegex(ExtractorError, 'DRM'):
                self.ie._real_extract('https://rplay.live/play/drm')
        hls.assert_not_called()

    def test_extensionless_server_hls_url_is_preserved_and_browser_user_agent_forwarded(self):
        from types import SimpleNamespace
        server_url = 'https://api.rplay.live/content/hlsstream?parent=true&s3key=authorized-key&token=synthetic-playback-token&userOid=0123456789abcdef01234567&contentOid=stream_test&loginType=google'
        metadata = {'title': 'Synthetic', 'canView': {'canView': True, 'url': server_url}}
        cookies = {'streamnest_rplay_user_agent': SimpleNamespace(value='Synthetic Browser/1.0')}
        with patch.object(self.ie, '_get_cookies', return_value=cookies), \
                patch.object(self.ie, '_download_json', side_effect=[metadata, None]) as json_request, \
                patch.object(self.ie, '_extract_m3u8_formats', return_value=[{'url': 'https://s3.rplay.live/media.m3u8', 'ext': 'mp4'}]) as hls_request:
            self.ie._real_extract('https://rplay.live/play/stream_test')
        self.assertEqual(hls_request.call_count, 1)
        self.assertEqual(hls_request.call_args.args[0], server_url)
        self.assertEqual(json_request.call_args_list[0].kwargs['headers']['User-Agent'], 'Synthetic Browser/1.0')
        self.assertEqual(hls_request.call_args.kwargs['headers']['User-Agent'], 'Synthetic Browser/1.0')

    def test_missing_server_stream_does_not_request_fabricated_hls_url(self):
        with patch.object(self.ie, '_download_json', side_effect=[{'title': 'Synthetic'}, None]), \
                patch.object(self.ie, '_extract_m3u8_formats') as hls_request:
            with self.assertRaises(ExtractorError):
                self.ie._real_extract('https://rplay.live/play/no_stream')
        hls_request.assert_not_called()

    def test_account_context_reaches_all_api_requests_without_cdn_leaks(self):
        from types import SimpleNamespace
        from urllib.parse import urlsplit, parse_qs
        cookies = {
            '_AUTHORIZATION_': SimpleNamespace(value='synthetic_token'),
            'streamnest_rplay_requestor': SimpleNamespace(value='0123456789abcdef01234567'),
            'streamnest_rplay_login_type': SimpleNamespace(value='google'),
        }
        requests = []
        def json_response(url, *args, **kwargs):
            requests.append(url)
            if '/download?' in url:
                return {'url': 'https://s3.rplay.live/media.mp4'}
            return {'title': 'Synthetic', 'files': []}
        def hls_response(url, *args, **kwargs):
            requests.append(url)
            return []
        with patch.object(self.ie, '_get_cookies', return_value=cookies), \
                patch.object(self.ie, '_download_json', side_effect=json_response), \
                patch.object(self.ie, '_extract_m3u8_formats', side_effect=hls_response):
            result = self.ie._real_extract('https://rplay.live/play/context_test')
        self.assertEqual(len(requests), 2)
        for url in requests:
            query = parse_qs(urlsplit(url).query)
            self.assertEqual(query['requestorOid'], ['0123456789abcdef01234567'])
            self.assertEqual(query['loginType'], ['google'])
            self.assertEqual(query['platformType'], ['rplay'])
            self.assertNotIn('synthetic_token', url)
        self.assertNotIn('requestorOid', result['formats'][0]['url'])
        self.assertNotIn('Authorization', result['formats'][0]['http_headers'])

    def setUp(self):
        self.ydl = YoutubeDL({'quiet': True})
        self.ie = module.StreamNestRPlayIE(self.ydl)

    def test_valid_url_matching(self):
        urls = [
            ('https://rplay.live/play/111111111111111111111111?playlist=purchaseList', '111111111111111111111111'),
            ('https://rplay.live/play/222222222222222222222222', '222222222222222222222222'),
            ('http://www.rplay.live/play/abc123_-XYZ', 'abc123_-XYZ'),
        ]
        for url, expected_id in urls:
            with self.subTest(url=url):
                self.assertTrue(self.ie.suitable(url))
                match = self.ie._match_valid_url(url)
                self.assertEqual(match.group('id'), expected_id)

    def test_invalid_url_matching(self):
        invalid_urls = [
            'https://rplay.live/explore',
            'https://rplay.live/home',
            'https://evil.rplay.live/play/123',
        ]
        for url in invalid_urls:
            with self.subTest(url=url):
                self.assertFalse(self.ie.suitable(url))

    def test_audio_only_content_extraction(self):
        video_id = '111111111111111111111111'
        content_info = {
            '_id': video_id,
            'title': 'Test Audio Drama',
            'length': 345.5,
            'isPaidContent': True,
            'user': {'nickname': 'AudioCreator'},
            'createdAt': '2025-01-01T00:00:00.000Z',
            'streamables': [
                {
                    'width': 0,
                    'height': 0,
                    's3key': f'content/{video_id}/audio.mp4',
                    'playtime': 345.5,
                }
            ],
            'files': [
                {
                    'name': 'audio.mp3',
                    'size': 12345678,
                }
            ],
        }
        download_info = {'url': 'https://s3.rplay.live/download/audio.m4a'}

        with patch.object(self.ie, '_download_json', side_effect=[content_info, download_info]), \
             patch.object(self.ie, '_extract_m3u8_formats', side_effect=ExtractorError('Forbidden', expected=True)), \
             patch.object(self.ie, '_request_webpage', return_value=MagicMock()):
            info = self.ie._real_extract(f'https://rplay.live/play/{video_id}')
            self.assertEqual(info['id'], video_id)
            self.assertEqual(info['title'], 'Test Audio Drama')
            self.assertEqual(info['uploader'], 'AudioCreator')
            self.assertTrue(len(info['formats']) >= 1)
            audio_format = info['formats'][0]
            self.assertEqual(audio_format['vcodec'], 'none')
            self.assertIn(audio_format['acodec'], ('aac', 'mp3', 'unknown'))

    def test_video_content_extraction(self):
        video_id = '222222222222222222222222'
        content_info = {
            '_id': video_id,
            'title': 'Test Video Stream',
            'canView': {'canView': True, 'url': 'https://api.rplay.live/content/hlsstream?parent=true&s3key=synthetic&token=synthetic'},
            'length': 600.0,
            'isPaidContent': True,
            'user': {'nickname': 'VideoCreator'},
            'createdAt': '2025-02-01T00:00:00.000Z',
            'streamables': [
                {
                    'width': 1920,
                    'height': 1080,
                    's3key': f'content/{video_id}/video_1080p.mp4',
                    'playtime': 600.0,
                }
            ],
        }

        mock_hls_formats = [
            {
                'format_id': 'hls-1080p',
                'height': 1080,
                'width': 1920,
                'fps': 30,
                'vcodec': 'avc1.640028',
                'acodec': 'mp4a.40.2',
                'url': 'https://s3.rplay.live/stream/chunk.m3u8',
            }
        ]

        with patch.object(self.ie, '_download_json', return_value=content_info), \
             patch.object(self.ie, '_extract_m3u8_formats', return_value=mock_hls_formats), \
             patch.object(self.ie, '_request_webpage', return_value=MagicMock()):
            info = self.ie._real_extract(f'https://rplay.live/play/{video_id}')
            self.assertEqual(info['id'], video_id)
            self.assertEqual(info['title'], 'Test Video Stream')
            self.assertEqual(info['uploader'], 'VideoCreator')
            self.assertEqual(info['formats'][0]['height'], 1080)
            self.assertEqual(info['formats'][0]['vcodec'], 'avc1.640028')

    def test_string_dimensions_handling(self):
        video_id = 'string_dim_id'
        content_info = {
            '_id': video_id,
            'title': 'String Dimensions Test',
            'length': 120.0,
            'streamables': [
                {
                    'width': '1920',
                    'height': '1080',
                }
            ],
            'canView': {
                'canView': True,
                'url': 'https://s3.rplay.live/content/video.mp4',
            }
        }
        with patch.object(self.ie, '_download_json', return_value=content_info), \
             patch.object(self.ie, '_extract_m3u8_formats', return_value=[]):
            info = self.ie._real_extract(f'https://rplay.live/play/{video_id}')
            self.assertTrue(len(info['formats']) >= 1)
            fmt = info['formats'][0]
            self.assertNotEqual(fmt['vcodec'], 'none')

    def test_contradictory_streamables_and_hls_retains_video(self):
        video_id = 'contradictory_id'
        content_info = {
            '_id': video_id,
            'title': 'Contradictory Streamables',
            'canView': {'canView': True, 'url': 'https://api.rplay.live/content/hlsstream?parent=true&s3key=synthetic&token=synthetic'},
            'streamables': [{}],  # Missing dimensions
            'files': [],
        }
        mock_hls_formats = [
            {
                'format_id': 'hls-1080p',
                'height': 1080,
                'width': 1920,
                'vcodec': 'avc1.640028',
                'acodec': 'mp4a.40.2',
                'url': 'https://s3.rplay.live/stream/chunk.m3u8',
            }
        ]
        with patch.object(self.ie, '_download_json', side_effect=[content_info, None]), \
             patch.object(self.ie, '_extract_m3u8_formats', return_value=mock_hls_formats):
            info = self.ie._real_extract(f'https://rplay.live/play/{video_id}')
            self.assertEqual(info['formats'][0]['vcodec'], 'avc1.640028')
            self.assertEqual(info['formats'][0]['height'], 1080)

    def test_signed_canview_url_extraction(self):
        video_id = 'signed_url_id'
        content_info = {
            '_id': video_id,
            'title': 'Signed URL Test',
            'length': 300.0,
            'canView': {
                'canView': True,
                'url': 'https://s3.rplay.live/content/audio.m4a?token=secret123&Expires=1700000000',
            }
        }
        with patch.object(self.ie, '_download_json', side_effect=[content_info, None]), \
             patch.object(self.ie, '_extract_m3u8_formats', return_value=[]):
            info = self.ie._real_extract(f'https://rplay.live/play/{video_id}')
            self.assertEqual(len(info['formats']), 1)
            self.assertEqual(info['formats'][0]['format_id'], 'canview-direct')
            self.assertEqual(info['formats'][0]['ext'], 'm4a')
            self.assertIn('Expires=1700000000', info['formats'][0]['url'])

    def test_missing_dimensions_with_direct_mp4_fallback_retains_video(self):
        video_id = 'missing_dim_mp4'
        content_info = {
            '_id': video_id,
            'title': 'Missing Dim Video',
            'streamables': [{}],  # Missing width and height
            'files': [],
        }
        direct_info = {'url': 'https://s3.rplay.live/content/video.mp4'}
        with patch.object(self.ie, '_download_json', side_effect=[content_info, direct_info]), \
             patch.object(self.ie, '_extract_m3u8_formats', side_effect=ExtractorError('No HLS', expected=True)):
            info = self.ie._real_extract(f'https://rplay.live/play/{video_id}')
            self.assertEqual(len(info['formats']), 1)
            fmt = info['formats'][0]
            self.assertEqual(fmt['ext'], 'mp4')
            self.assertNotEqual(fmt['vcodec'], 'none')

    def test_authenticated_request_construction(self):
        video_id = 'auth_token_test'
        content_info = {
            '_id': video_id,
            'title': 'Auth Token Content',
            'length': 100.0,
            'canView': {'canView': True, 'url': 'https://s3.rplay.live/content/media.mp4'}
        }
        mock_cookie = MagicMock()
        mock_cookie.value = 'secret_jwt_token_12345'

        with patch.object(self.ie, '_get_cookies', return_value={'_AUTHORIZATION_': mock_cookie}), \
             patch.object(self.ie, '_download_json', return_value=content_info) as mock_dl_json, \
             patch.object(self.ie, '_extract_m3u8_formats', side_effect=ExtractorError('No HLS', expected=True)):
            info = self.ie._real_extract(f'https://rplay.live/play/{video_id}')
            # Verify _download_json received the Authorization header
            called_headers = mock_dl_json.call_args[1].get('headers') or {}
            self.assertEqual(called_headers.get('Authorization'), 'secret_jwt_token_12345')
            # Verify format on CDN does NOT leak Authorization header
            fmt = info['formats'][0]
            self.assertNotIn('Authorization', fmt.get('http_headers', {}))

    def test_is_api_host_and_credential_scoping(self):
        _is_api_host = module._is_api_host
        self.assertTrue(_is_api_host('https://api.rplay.live/content/hlsstream'))
        self.assertTrue(_is_api_host('https://api.rplay.live:443/content/hlsstream'))
        # Non-default ports must be rejected
        self.assertFalse(_is_api_host('https://api.rplay.live:8443/content/hlsstream'))
        self.assertFalse(_is_api_host('https://api.rplay.live:8080/content/hlsstream'))
        # Userinfo must be rejected
        self.assertFalse(_is_api_host('https://user:pass@api.rplay.live/content'))
        # HTTP scheme must be rejected
        self.assertFalse(_is_api_host('http://api.rplay.live/content/stream.m3u8'))
        # Attacker subdomains and other domains must be rejected
        self.assertFalse(_is_api_host('https://api.rplay.live.attacker.com/stream.m3u8'))
        self.assertFalse(_is_api_host('https://attacker.com/api.rplay.live/stream.m3u8'))
        self.assertFalse(_is_api_host('https://s3.rplay.live/content/stream.m3u8'))

    def test_audio_only_hls_preserves_audio_classification(self):
        video_id = 'hls_audio_id'
        content_info = {
            '_id': video_id,
            'title': 'HLS Audio Only Content',
            'canView': {'canView': True, 'url': 'https://api.rplay.live/content/hlsstream?parent=true&s3key=synthetic&token=synthetic'},
            'streamables': [{'width': 0, 'height': 0}],
            'files': [],
        }
        hls_fmt = {
            'url': 'https://api.rplay.live/content/stream.m3u8',
            'format_id': 'hls-audio',
            'ext': 'mp4',
            'vcodec': 'none',
            'width': 0,
            'height': 0
        }
        mock_cookie = MagicMock()
        mock_cookie.value = 'valid_token'
        with patch.object(self.ie, '_get_cookies', return_value={'_AUTHORIZATION_': mock_cookie}), \
             patch.object(self.ie, '_download_json', return_value=content_info), \
             patch.object(self.ie, '_extract_m3u8_formats', return_value=[hls_fmt]):
            info = self.ie._real_extract(f'https://rplay.live/play/{video_id}')
            self.assertEqual(len(info['formats']), 1)
            fmt = info['formats'][0]
            self.assertEqual(fmt['vcodec'], 'none')
            self.assertEqual(fmt['ext'], 'm4a')
            # Formats never store raw Authorization in http_headers to prevent CDN segment leaks
            self.assertNotIn('Authorization', fmt.get('http_headers', {}))

    def test_request_director_scoping_and_cdn_protection(self):
        from yt_dlp.networking import Request
        auth_token = 'jwt_secret_token_abc'
        self.ie._setup_request_scoping(auth_token)
        rd = self.ydl._request_director
        mock_underlying_send = MagicMock(return_value=MagicMock())
        rd._streamnest_orig_send = mock_underlying_send

        # 1. API request gets Authorization attached
        api_req = Request('https://api.rplay.live/content/hlsstream?contentOid=123')
        rd.send(api_req)
        self.assertEqual(api_req.headers.get('Authorization'), auth_token)

        # 2. CDN segment request strips Authorization and Cookie header, and sets empty cookiejar
        cdn_req = Request('https://s3.rplay.live/content/segment0.ts', headers={'Authorization': 'leak_attempt', 'Cookie': '_AUTHORIZATION_=leak'})
        rd.send(cdn_req)
        self.assertNotIn('Authorization', cdn_req.headers)
        self.assertNotIn('authorization', cdn_req.headers)
        self.assertNotIn('Cookie', cdn_req.headers)
        self.assertNotIn('cookie', cdn_req.headers)
        # Empty cookiejar prevents transport layer from attaching auth cookies
        self.assertIn('cookiejar', cdn_req.extensions)
        self.assertEqual(len(cdn_req.extensions['cookiejar']), 0)

        # 3. Key request on pb3 CDN strips Authorization and Cookie
        key_req = Request('https://pb3.rplay.live/content/key.bin', headers={'Authorization': 'leak_attempt', 'Cookie': 'token=leak'})
        rd.send(key_req)
        self.assertNotIn('Authorization', key_req.headers)
        self.assertNotIn('Cookie', key_req.headers)
        self.assertEqual(len(key_req.extensions['cookiejar']), 0)

        # 4. Attacker domain strips Authorization and Cookie
        attacker_req = Request('https://api.rplay.live.attacker.example/stream.m3u8', headers={'Authorization': 'leak_attempt'})
        rd.send(attacker_req)
        self.assertNotIn('Authorization', attacker_req.headers)

    def test_token_contract_case_insensitive_and_strict(self):
        for accepted_name in ('_AUTHORIZATION_', '_authorization_', 'Authorization', 'token', 'access_token'):
            with self.subTest(name=accepted_name):
                c = MagicMock()
                c.value = 'token_val'
                with patch.object(self.ie, '_get_cookies', return_value={accepted_name: c}):
                    self.assertEqual(self.ie._extract_auth_token(), 'token_val')

        # Rejected names (refresh tokens or session IDs are not bearer credentials)
        for rejected_name in ('refresh_token', 'refresh-token', 'refreshToken', 'session', 'connect.sid'):
            with self.subTest(rejected=rejected_name):
                c = MagicMock()
                c.value = 'refresh_val'
                with patch.object(self.ie, '_get_cookies', return_value={rejected_name: c}):
                    self.assertIsNone(self.ie._extract_auth_token())

    def test_unrelated_api_cookie_falls_back_to_web_cookie(self):
        video_id = 'cookie_fallback_test'
        content_info = {
            '_id': video_id,
            'title': 'Cookie Fallback',
            'canView': {'canView': True, 'url': 'https://s3.rplay.live/content/media.mp4'}
        }
        unrelated_api_cookie = MagicMock()
        unrelated_api_cookie.value = 'ga_12345'
        valid_auth_cookie = MagicMock()
        valid_auth_cookie.value = 'fallback_token_jwt'

        def get_cookies_mock(origin):
            if origin == 'https://api.rplay.live':
                return {'_ga': unrelated_api_cookie}
            elif origin == 'https://rplay.live':
                return {'_AUTHORIZATION_': valid_auth_cookie}
            return {}

        with patch.object(self.ie, '_get_cookies', side_effect=get_cookies_mock), \
             patch.object(self.ie, '_download_json', return_value=content_info) as mock_dl, \
             patch.object(self.ie, '_extract_m3u8_formats', side_effect=ExtractorError('No HLS', expected=True)):
            self.ie._real_extract(f'https://rplay.live/play/{video_id}')
            called_headers = mock_dl.call_args[1].get('headers') or {}
            self.assertEqual(called_headers.get('Authorization'), 'fallback_token_jwt')

    def test_http_401_and_403_raises_login_required(self):
        video_id = 'auth_error_id'
        try:
            from yt_dlp.networking.exceptions import HTTPError as YtDlpHTTPError
            for status_code in (401, 403):
                with self.subTest(status_code=status_code):
                    cause = MagicMock(spec=YtDlpHTTPError)
                    cause.status = status_code
                    cause.code = status_code
                    extractor_err = ExtractorError(f'HTTP Error {status_code}', cause=cause)
                    with patch.object(self.ie, '_download_json', side_effect=extractor_err):
                        with self.assertRaises(ExtractorError) as ctx:
                            self.ie._real_extract(f'https://rplay.live/play/{video_id}')
                        self.assertIn('로그인', str(ctx.exception))
        except ImportError:
            pass

        # Also test standard urllib.error.HTTPError
        import urllib.error
        for status_code in (401, 403):
            with self.subTest(urllib_status_code=status_code):
                http_err = urllib.error.HTTPError('https://api.rplay.live', status_code, 'Denied', {}, None)
                extractor_err = ExtractorError(f'HTTP Error {status_code}', cause=http_err)
                with patch.object(self.ie, '_download_json', side_effect=extractor_err):
                    with self.assertRaises(ExtractorError) as ctx:
                        self.ie._real_extract(f'https://rplay.live/play/{video_id}')
                    self.assertIn('로그인', str(ctx.exception))

    def test_login_required_raised_for_unauthorized_content(self):
        video_id = 'unauthorized_id'
        content_info = {
            '_id': video_id,
            'title': 'Paid Content Without Access',
            'isPaidContent': True,
            'streamables': [],
            'files': [],
        }

        with patch.object(self.ie, '_download_json', return_value=content_info), \
             patch.object(self.ie, '_extract_m3u8_formats', side_effect=ExtractorError('Forbidden', expected=True)), \
             patch.object(self.ie, '_request_webpage', side_effect=ExtractorError('Unauthorized', expected=True)):
            with self.assertRaises(ExtractorError) as ctx:
                self.ie._real_extract(f'https://rplay.live/play/{video_id}')
            self.assertIn('로그인', str(ctx.exception))

    def test_handler_check_extensions_accepts_isolated_jar_and_rejects_generic_jar(self):
        import http.cookiejar
        from yt_dlp.networking._urllib import UrllibRH
        from yt_dlp.networking._requests import RequestsRH

        isolated_jar = module._IsolatedEmptyCookieJar()
        generic_jar = http.cookiejar.CookieJar()

        for RH in (UrllibRH, RequestsRH):
            rh = RH(logger=None)
            # Must accept _IsolatedEmptyCookieJar
            rh._check_extensions({'cookiejar': isolated_jar})
            # Must raise AssertionError on generic http.cookiejar.CookieJar
            with self.assertRaises(AssertionError):
                rh._check_extensions({'cookiejar': generic_jar})

    def test_isolated_empty_cookie_jar_cannot_accumulate_or_send(self):
        import http.cookiejar, urllib.request
        jar = module._IsolatedEmptyCookieJar()
        cookie = http.cookiejar.Cookie(0, 'token', 'val', None, False, 'api.rplay.live', False, False, '/', True, False, None, False, None, None, {})
        jar.set_cookie(cookie)
        self.assertEqual(len(jar), 0)

        mock_resp = MagicMock()
        mock_req = urllib.request.Request('https://api.rplay.live/foo')
        jar.extract_cookies(mock_resp, mock_req)
        self.assertEqual(len(jar), 0)

        out_req = urllib.request.Request('https://api.rplay.live/foo')
        jar.add_cookie_header(out_req)
        self.assertIsNone(out_req.get_header('Cookie'))

    def test_is_api_host_strict_userinfo_and_port(self):
        # Rejects any userinfo, including empty
        self.assertFalse(module._is_api_host('https://@api.rplay.live/'))
        self.assertFalse(module._is_api_host('https://:@api.rplay.live/'))
        self.assertFalse(module._is_api_host('https://user:pass@api.rplay.live/'))
        self.assertFalse(module._is_api_host('https://user@api.rplay.live/'))

        # Rejects non-default ports
        self.assertFalse(module._is_api_host('https://api.rplay.live:8443/'))
        self.assertFalse(module._is_api_host('https://api.rplay.live:8080/'))

        # Accepts exact default HTTPS
        self.assertTrue(module._is_api_host('https://api.rplay.live/'))
        self.assertTrue(module._is_api_host('https://api.rplay.live:443/'))
        self.assertTrue(module._is_api_host('https://api.rplay.live/graphql'))

    def test_rplay_exact_origin_cookie_policy_return_and_set_ok(self):
        import http.cookiejar, urllib.request
        policy = module._RPlayExactOriginCookiePolicy()
        c_exact = http.cookiejar.Cookie(0, 'token', 'sec', None, False, 'api.rplay.live', False, False, '/', True, False, None, False, None, None, {})
        c_wildcard = http.cookiejar.Cookie(0, 'token', 'sec', None, False, '.rplay.live', True, True, '/', True, False, None, False, None, None, {})

        # return_ok
        req_api = urllib.request.Request('https://api.rplay.live/play')
        req_cdn = urllib.request.Request('https://pb3.rplay.live/seg.ts')
        req_port = urllib.request.Request('https://api.rplay.live:8443/play')
        req_userinfo = urllib.request.Request('https://@api.rplay.live/play')

        self.assertTrue(policy.return_ok(c_exact, req_api))
        self.assertFalse(policy.return_ok(c_exact, req_cdn))
        self.assertFalse(policy.return_ok(c_exact, req_port))
        self.assertFalse(policy.return_ok(c_exact, req_userinfo))

        # set_ok prevents non-api and wildcard response cookies
        self.assertTrue(policy.set_ok(c_exact, req_api))
        self.assertFalse(policy.set_ok(c_wildcard, req_api))
        self.assertFalse(policy.set_ok(c_exact, req_cdn))
        self.assertFalse(policy.set_ok(c_exact, req_port))

    def test_requests_rebuild_auth_hook_strips_headers_on_non_api_redirect(self):
        import requests
        from yt_dlp.networking._requests import RequestsSession

        self.ie._setup_request_scoping('auth_token_xyz')
        session = RequestsSession()

        req_origin = requests.Request('GET', 'https://api.rplay.live/play').prepare()
        resp = requests.Response()
        resp.request = req_origin
        resp.status_code = 302

        # 1. Redirect to CDN
        prep_cdn = requests.Request('GET', 'https://pb3.rplay.live/stream.m3u8').prepare()
        prep_cdn.headers['Authorization'] = 'Bearer token'
        prep_cdn.headers['Cookie'] = 'token=secret'
        session.rebuild_auth(prep_cdn, resp)
        self.assertNotIn('Authorization', prep_cdn.headers)
        self.assertNotIn('Cookie', prep_cdn.headers)

        # 2. Redirect to non-default port
        prep_port = requests.Request('GET', 'https://api.rplay.live:8443/stream.m3u8').prepare()
        prep_port.headers['Authorization'] = 'Bearer token'
        prep_port.headers['Cookie'] = 'token=secret'
        session.rebuild_auth(prep_port, resp)
        self.assertNotIn('Authorization', prep_port.headers)
        self.assertNotIn('Cookie', prep_port.headers)

        # 3. Redirect to valid API origin preserves auth
        prep_api = requests.Request('GET', 'https://api.rplay.live/graphql').prepare()
        prep_api.headers['Authorization'] = 'Bearer token'
        prep_api.headers['Cookie'] = 'token=secret'
        session.rebuild_auth(prep_api, resp)
        self.assertEqual(prep_api.headers.get('Authorization'), 'Bearer token')
        self.assertEqual(prep_api.headers.get('Cookie'), 'token=secret')

    def test_setup_request_scoping_with_empty_cookiejar(self):
        import http.cookiejar
        empty_cj = http.cookiejar.CookieJar()
        self.ydl.cookiejar = empty_cj
        self.ie._setup_request_scoping('test_token')
        # Policy must be installed even on initially empty jar
        self.assertIsInstance(empty_cj._policy, module._RPlayExactOriginCookiePolicy)

    def test_empty_jar_set_cookie_and_redirect_scoping(self):
        import http.cookiejar
        import urllib.request
        import http.client
        from io import BytesIO
        from yt_dlp.networking._requests import RequestsSession
        import requests

        empty_cj = http.cookiejar.CookieJar()
        self.ydl.cookiejar = empty_cj
        self.ie._setup_request_scoping('initial_token')

        def _make_response(headers_str, url):
            hdrs = http.client.parse_headers(BytesIO(headers_str.encode('utf-8')))
            return urllib.response.addinfourl(BytesIO(b''), hdrs, url)

        # 1. Realistic Set-Cookie via extract_cookies (verifies set_ok policy)
        # A. Valid host-only cookie on api.rplay.live is accepted by set_ok
        req_api = urllib.request.Request('https://api.rplay.live/api/auth')
        resp_api = _make_response('Set-Cookie: _AUTHORIZATION_=fresh_session_jwt; Path=/; Secure\r\n\r\n', 'https://api.rplay.live/api/auth')
        empty_cj.extract_cookies(resp_api, req_api)
        self.assertEqual(len(empty_cj), 1)

        # B. Wildcard domain cookie (.rplay.live) is rejected by set_ok
        resp_wildcard = _make_response('Set-Cookie: _AUTHORIZATION_=wildcard_jwt; Domain=.rplay.live; Path=/; Secure\r\n\r\n', 'https://api.rplay.live/api/auth')
        empty_cj.extract_cookies(resp_wildcard, req_api)
        self.assertEqual(len(list(empty_cj)), 1)
        self.assertEqual(list(empty_cj)[0].value, 'fresh_session_jwt')

        # C. Non-api domain (rplay.live) is rejected by set_ok
        resp_sub = _make_response('Set-Cookie: _AUTHORIZATION_=bad_jwt; Domain=rplay.live; Path=/; Secure\r\n\r\n', 'https://api.rplay.live/api/auth')
        empty_cj.extract_cookies(resp_sub, req_api)
        self.assertEqual(len(list(empty_cj)), 1)

        # D. Non-api request origin cannot set rplay cookie
        req_cdn = urllib.request.Request('https://s3.rplay.live/content/123')
        resp_cdn = _make_response('Set-Cookie: _AUTHORIZATION_=cdn_jwt; Path=/; Secure\r\n\r\n', 'https://s3.rplay.live/content/123')
        empty_cj.extract_cookies(resp_cdn, req_cdn)
        self.assertEqual(len(list(empty_cj)), 1)

        # E. Foreign domain (attacker.com) is rejected
        resp_foreign = _make_response('Set-Cookie: _AUTHORIZATION_=foreign_jwt; Domain=attacker.com; Path=/; Secure\r\n\r\n', 'https://api.rplay.live/api/auth')
        empty_cj.extract_cookies(resp_foreign, req_api)
        self.assertEqual(len(list(empty_cj)), 1)

        # 2. Outgoing requests via add_cookie_header (verifies return_ok policy)
        # Cookie is added to api.rplay.live requests
        req_out_api = urllib.request.Request('https://api.rplay.live/content/123')
        empty_cj.add_cookie_header(req_out_api)
        self.assertTrue(req_out_api.has_header('Cookie'))
        self.assertIn('_AUTHORIZATION_=fresh_session_jwt', req_out_api.get_header('Cookie'))

        # Cookie is NOT added to CDN or other hosts
        req_out_cdn = urllib.request.Request('https://s3.rplay.live/content/123')
        empty_cj.add_cookie_header(req_out_cdn)
        self.assertFalse(req_out_cdn.has_header('Cookie'))
        self.assertIsNone(req_out_cdn.get_header('Cookie'))

        req_out_web = urllib.request.Request('https://rplay.live/play/123')
        empty_cj.add_cookie_header(req_out_web)
        self.assertFalse(req_out_web.has_header('Cookie'))
        self.assertIsNone(req_out_web.get_header('Cookie'))

        # 3. Verify session rebuild_auth strips cookies on redirect to CDN
        session = RequestsSession()
        req_origin = requests.Request('GET', 'https://api.rplay.live/content').prepare()
        resp = requests.Response()
        resp.request = req_origin
        resp.status_code = 302

        prep_cdn = requests.Request('GET', 'https://s3.rplay.live/stream.m3u8').prepare()
        prep_cdn.headers['Authorization'] = 'Bearer token'
        prep_cdn.headers['Cookie'] = '_AUTHORIZATION_=fresh_session_jwt'
        session.rebuild_auth(prep_cdn, resp)
        self.assertNotIn('Authorization', prep_cdn.headers)
        self.assertNotIn('Cookie', prep_cdn.headers)


if __name__ == '__main__':
    unittest.main()
