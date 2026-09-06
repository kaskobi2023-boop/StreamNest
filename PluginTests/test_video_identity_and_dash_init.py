import importlib.util
import json
import pathlib
import sys
import types
import unittest
import xml.etree.ElementTree as ET
from unittest.mock import patch

sys.dont_write_bytecode = True
from yt_dlp import YoutubeDL
from yt_dlp.extractor.generic import GenericIE

PLUGIN_ROOT = pathlib.Path(__file__).resolve().parents[1] / 'ChzzkDownloader/Tools/yt-dlp-plugins/streamnest/yt_dlp_plugins/extractor'


def load(name):
    spec = importlib.util.spec_from_file_location('identity_test_' + name, PLUGIN_ROOT / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


packed = load('streamnest_packed')
chzzk = load('streamnest_chzzk')


class IdentityAndInitializationTests(unittest.TestCase):
    def grouped(self, shared_audio, shared_video=False, generic_duplicate=False):
        page = 'https://example.org/watch'
        source = 'var a="/a/master.m3u8";var b="/b/master.m3u8";'
        html = '<script>eval(function(p,a,c,k,e,d){return p;}(' + json.dumps(source) + ',36,0,"".split("|"),0,{}))</script>'
        with YoutubeDL({'quiet': True, 'extractor_args': {'generic': {'streamnest_packed': ['true']}}}) as ydl:
            ie = packed._StreamNestPackedGenericIE(ydl)

            def download(url, *args, **kwargs):
                name = 'a' if '/a/' in url else 'b'
                audio = '/shared/audio.m3u8' if shared_audio else f'/{name}/audio.m3u8'
                video = '/shared/video.m3u8' if shared_video else f'/{name}/video.m3u8'
                manifest = ('#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="audio",NAME="Main",DEFAULT=YES,URI="'
                            + audio + '"\n#EXT-X-STREAM-INF:BANDWIDTH=500000,RESOLUTION=1280x720,CODECS="avc1.42c01e,mp4a.40.2",AUDIO="audio"\n'
                            + video + '\n')
                return manifest, types.SimpleNamespace(url=url)

            generic_entries = []
            if generic_duplicate:
                text, response = download('https://example.org/a/master.m3u8')
                formats, _ = ie._parse_m3u8_formats_and_subtitles(text, response.url, 'mp4')
                generic_entries.append({'id': 'generic-copy', 'formats': formats})
            with patch.object(GenericIE, '_extract_embeds', return_value=iter(generic_entries)), \
                 patch.object(ie, '_download_webpage_handle', side_effect=download), \
                 patch.object(ie, '_extra_manifest_info', return_value=None):
                # Use yt-dlp's real HLS parser; only network is replaced.
                return list(ie._extract_embeds(page, html, info_dict={'id': 'fixture', 'title': 'Two videos'}))

    def test_shared_audio_does_not_merge_distinct_videos(self):
        entries = self.grouped(shared_audio=True)
        self.assertEqual(2, len(entries))
        for entry in entries:
            self.assertEqual(1, len([f for f in entry['formats'] if f.get('vcodec') != 'none']))
            self.assertTrue(any(f.get('vcodec') == 'none' for f in entry['formats']))

    def test_disjoint_videos_remain_separate(self):
        self.assertEqual(2, len(self.grouped(shared_audio=False)))

    def test_shared_video_still_merges_and_retains_audio(self):
        entry, = self.grouped(shared_audio=True, shared_video=True)
        self.assertEqual(2, len(entry['formats']))

    def test_generic_duplicate_with_audio_is_not_reintroduced(self):
        entries = self.grouped(shared_audio=True, generic_duplicate=True)
        self.assertEqual(2, len(entries))
        self.assertFalse(any(e['id'] == 'generic-copy' for e in entries))

    def range_formats(self, initialization):
        document = ET.fromstring('<MPD><Period><AdaptationSet mimeType="video/mp4" codecs="avc1.42c01e"><Representation id="v" height="720">'
            '<BaseURL>video.mp4</BaseURL><SegmentList>' + initialization + '<SegmentURL mediaRange="100-999"/>'
            '</SegmentList></Representation></AdaptationSet></Period></MPD>')
        with YoutubeDL({'quiet': True}) as ydl:
            ie = chzzk._StreamNestCHZZKVideoIE(ydl)
            with patch.object(ie, '_download_xml_handle', return_value=(document, types.SimpleNamespace(url='https://example.org/dash/manifest.mpd'))), \
                 patch.object(ie, '_parse_mpd_formats_and_subtitles', return_value=([], {})):
                formats, _ = ie._extract_range_mpd_formats('https://example.org/dash/manifest.mpd', 'fixture')
                return formats[0]

    def test_source_only_initialization_precedes_media(self):
        result = self.range_formats('<Initialization sourceURL="init.mp4"/>')
        self.assertEqual({'url': 'https://example.org/dash/init.mp4'}, result['fragments'][0])
        self.assertEqual(2, len(result['fragments']))
        self.assertIsNone(result['filesize'])  # Whole initialization size is unknown.

    def test_source_and_range_initialization_preserved(self):
        result = self.range_formats('<Initialization sourceURL="init.mp4" range="0-99"/>')
        self.assertEqual({'url': 'https://example.org/dash/init.mp4', 'byte_range': {'start': 0, 'end': 100}}, result['fragments'][0])
        self.assertEqual(1000, result['filesize'])

    def test_range_only_initialization_preserved(self):
        result = self.range_formats('<Initialization range="0-99"/>')
        self.assertEqual({'url': 'https://example.org/dash/video.mp4', 'byte_range': {'start': 0, 'end': 100}}, result['fragments'][0])


if __name__ == '__main__':
    unittest.main()
