import importlib.util
import json
import pathlib
import re
import socket
import sys
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
PLUGIN = pathlib.Path(__file__).resolve().parents[1] / 'ChzzkDownloader/Tools/yt-dlp-plugins/streamnest/yt_dlp_plugins/extractor/streamnest_packed.py'
spec = importlib.util.spec_from_file_location('packed_hls_under_test', PLUGIN)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
PackedHls = module._PackedHls


def packed(payload, base=36, count=2, symbols='var|source'):
    # A self-contained data fixture; decoding must never execute the body.
    return ("eval(function(p,a,c,k,e,d){throw Error('DO NOT EXECUTE');}("
            + f'{json.dumps(payload)},{base},{count},{json.dumps(symbols)}.split("|"),0,{{}}))')


class PackedHlsTests(unittest.TestCase):
    def test_word_substitution_and_quoted_url(self):
        result = list(PackedHls.unpack(packed('0 1="https://example.org/a.m3u8";')))
        self.assertEqual(['var source="https://example.org/a.m3u8";'], result)

    def test_decoder_never_calls_eval(self):
        with patch('builtins.eval', side_effect=AssertionError('JavaScript must not be evaluated')):
            self.assertEqual(1, len(list(PackedHls.unpack(packed('0 1="/a.m3u8";')))))

    def test_relative_query_and_escaped_slashes(self):
        html = '<script>' + packed(r'0 1="\/media\/a.m3u8?token=fixture";') + '</script>'
        self.assertEqual(['https://example.org/media/a.m3u8?token=fixture'],
                         PackedHls.candidates(html, 'https://example.org/player/watch'))

    def test_multiple_blocks_deduplicate(self):
        script = '<script>' + packed('0 1="/a.m3u8";') + '</script>'
        self.assertEqual(['https://example.org/a.m3u8'], PackedHls.candidates(script * 2, 'https://example.org/watch'))

    def test_nested_packer(self):
        inner = packed('0 1="/nested.m3u8";')
        html = '<script>' + packed(inner, count=0, symbols='') + '</script>'
        self.assertEqual(['https://example.org/nested.m3u8'], PackedHls.candidates(html, 'https://example.org/watch'))

    def test_plain_javascript_is_not_executed_or_treated_as_packer(self):
        self.assertEqual([], PackedHls.candidates('<script>fetch("/a.m3u8")</script>', 'https://example.org/watch'))

    def test_invalid_packer_is_skipped(self):
        for script in (packed('0 1', base=1), packed('0', base=63), packed('0', count=4),
                       packed('0', count=5000), "eval(function(p,a,c,k,e,d){broken"):
            with self.subTest(script=script[:70]):
                self.assertEqual([], list(PackedHls.unpack(script)))

    def test_size_and_expansion_limits(self):
        self.assertEqual([], list(PackedHls.unpack('x' * (PackedHls.MAX_SCRIPT + 1))))
        self.assertEqual([], PackedHls.candidates('x' * (PackedHls.MAX_PAGE + 1), 'https://example.org'))
        self.assertEqual([], list(PackedHls.unpack(packed('0 ' * 10000, count=1, symbols='x' * 200))))

    def test_no_url_from_unquoted_or_fragment_values(self):
        html = '<script>' + packed('0 1="/a.m3u8#not-sent"; other="hello";') + '</script>'
        self.assertEqual([], PackedHls.candidates(html, 'https://example.org/watch'))

    def test_origin_headers_do_not_leak_query_to_other_hosts(self):
        page = 'https://example.org/watch?private=fixture#fragment'
        self.assertEqual({'Referer': 'https://example.org/watch?private=fixture', 'Origin': 'https://example.org'},
                         PackedHls.headers(page, 'https://example.org/a.m3u8'))
        self.assertEqual({'Referer': 'https://example.org/', 'Origin': 'https://example.org'},
                         PackedHls.headers(page, 'https://cdn.example.org/a.m3u8'))

    def test_public_cross_origin_stream(self):
        answer = [(socket.AF_INET, socket.SOCK_STREAM, 6, '', ('93.184.215.14', 443))]
        with patch.object(socket, 'getaddrinfo', return_value=answer):
            self.assertTrue(PackedHls.allowed_url('https://cdn.example.org/a.m3u8', 'https://example.org/watch'))

    def test_private_or_failed_dns_is_rejected(self):
        for address in ('127.0.0.1', '10.0.0.1', '169.254.169.254', '::1', 'fc00::1', '203.0.113.1'):
            answer = [(socket.AF_INET, socket.SOCK_STREAM, 6, '', (address, 443))]
            with patch.object(socket, 'getaddrinfo', return_value=answer):
                self.assertFalse(PackedHls.allowed_url('https://cdn.example.org/a.m3u8', 'https://example.org/watch'))
        with patch.object(socket, 'getaddrinfo', side_effect=socket.gaierror()):
            self.assertFalse(PackedHls.allowed_url('https://cdn.example.org/a.m3u8', 'https://example.org/watch'))

    def test_unsafe_urls_are_rejected(self):
        for url in ('file:///a.m3u8', 'javascript:alert(1)', 'https://user:password@example.org/a.m3u8',
                    'https://cdn.example.org:8443/a.m3u8', 'http://cdn.example.org/a.m3u8',
                    'https://cdn.example.org/a\r\n.m3u8', 'https://example.org\\@127.0.0.1/a.m3u8'):
            with self.subTest(url=url):
                self.assertFalse(PackedHls.allowed_url(url, 'https://example.org/watch'))

    def test_same_origin_uses_already_validated_page_origin(self):
        with patch.object(socket, 'getaddrinfo', side_effect=AssertionError('No extra DNS for same origin')):
            self.assertTrue(PackedHls.allowed_url('https://example.org/a.m3u8', 'https://example.org/watch'))

    def test_video_id_is_stable_filename_safe_and_does_not_expose_queries(self):
        page = 'https://example.org/watch?private=page-token'
        stream = 'https://example.org/a.m3u8?private=stream-token'
        first = PackedHls.video_id(page, stream)
        self.assertRegex(first, r'^web-packed-[a-f0-9]{20}$')
        self.assertEqual(first, PackedHls.video_id(page, stream))
        self.assertNotEqual(first, PackedHls.video_id(page, stream + '-changed'))

    def test_transport_token_rotation_does_not_change_video_identity(self):
        first = PackedHls.video_id('https://example.org/watch?video=1&token=old',
                                   'https://cdn.example.org/movie/180p/index.m3u8?token=old&expires=1')
        second = PackedHls.video_id('https://example.org/watch?token=new&video=1',
                                    'https://cdn.example.org/movie/90p/index.m3u8?expires=2&token=new')
        self.assertEqual(first, second)
        self.assertNotEqual(first, PackedHls.video_id('https://example.org/watch?video=2',
                                                     'https://cdn.example.org/movie/90p/index.m3u8'))

    def test_unrelated_streams_are_not_grouped(self):
        self.assertNotEqual(PackedHls.quality_key('https://example.org/trailer/180p/index.m3u8'),
                            PackedHls.quality_key('https://example.org/main/90p/index.m3u8'))
        self.assertNotEqual(PackedHls.quality_key('https://example.org/video.m3u8?video=1'),
                            PackedHls.quality_key('https://example.org/video.m3u8?video=2'))

    def test_cross_origin_page_path_requires_opt_in_and_excludes_query(self):
        page = 'https://example.org/watch/123?secret=keep#fragment'
        media = 'https://cdn.example.org/main.m3u8'
        self.assertEqual('https://example.org/', PackedHls.headers(page, media)['Referer'])
        headers = PackedHls.headers(page, media, include_page_path=True)
        self.assertEqual('https://example.org/watch/123', headers['Referer'])
        self.assertEqual('https://example.org', headers['Origin'])
        self.assertNotIn('secret', str(headers))


if __name__ == '__main__':
    unittest.main()
