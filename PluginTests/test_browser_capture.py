import json
import os
import pathlib
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / 'ChzzkDownloader/Tools/yt-dlp-plugins/streamnest'))
from yt_dlp import YoutubeDL
from yt_dlp.utils import ExtractorError
from yt_dlp_plugins.extractor.streamnest_browser import StreamNestBrowserIE

PAGE = 'https://example.org/watch?video=1'
MASTER = '#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=200000,RESOLUTION=320x180,CODECS="avc1.42c01e,mp4a.40.2"\n180p/index.m3u8\n'
CHILD = '#EXTM3U\n#EXT-X-TARGETDURATION:4\n#EXTINF:4,\nchunk.bin\n#EXT-X-ENDLIST\n'


class BrowserCaptureTests(unittest.TestCase):
    def extract(self, media, child=CHILD):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / ('capture-' + 'a' * 32 + '.json')
            path.write_text(json.dumps({'PageUrl': PAGE, 'Title': 'Fixture', 'Html': '', 'Media': media}), encoding='utf-8')
            with YoutubeDL({'quiet': True}) as ydl, patch.dict(os.environ, STREAMNEST_BROWSER_CAPTURE=str(path)):
                ie = StreamNestBrowserIE(ydl)
                with patch.object(ie, '_download_webpage', return_value=child):
                    return ie._real_extract('streamnest-browser:' + 'a' * 32)['entries']

    def test_extensionless_master_has_duration_and_quality(self):
        entry, = self.extract([{'Url': 'https://example.org/hls/manifest', 'Manifest': MASTER}])
        self.assertEqual(180, entry['formats'][0]['height'])
        self.assertEqual(4, entry['duration'])
        self.assertTrue(entry['streamnest_browser'])

    def test_master_and_child_do_not_duplicate_quality_or_change_identity(self):
        master = {'Url': 'https://example.org/hls/manifest?token=old', 'Manifest': MASTER}
        child = {'Url': 'https://example.org/hls/180p/index.m3u8', 'Manifest': CHILD}
        original, = self.extract([master])
        combined, = self.extract([child, master])
        self.assertEqual(original['id'], combined['id'])
        self.assertEqual(1, len(combined['formats']))
        self.assertEqual(180, combined['formats'][0]['height'])
        master['Url'] = master['Url'].replace('old', 'new')
        refreshed, = self.extract([master, child])
        self.assertEqual(original['id'], refreshed['id'])

    def test_live_child_of_master_is_excluded(self):
        with self.assertRaises(ExtractorError):
            self.extract([{'Url': 'https://example.org/master', 'Manifest': MASTER}], CHILD.replace('#EXT-X-ENDLIST', ''))

    def test_drm_child_of_master_is_excluded(self):
        with self.assertRaises(ExtractorError):
            self.extract([{'Url': 'https://example.org/master', 'Manifest': MASTER}],
                         CHILD + '#EXT-X-KEY:METHOD=SAMPLE-AES,URI="key"\n')

    def test_private_key_target_is_excluded(self):
        with self.assertRaises(ExtractorError):
            self.extract([{'Url': 'https://example.org/playlist', 'Manifest': CHILD +
                         '#EXT-X-KEY:METHOD=AES-128,URI="http://127.0.0.1/key"\n'}])

    def test_public_aes_key_is_accepted_without_credentials(self):
        entry, = self.extract([{'Url': 'https://example.org/playlist', 'Manifest': CHILD +
                              '#EXT-X-KEY:METHOD=AES-128,URI="key"\n'}])
        self.assertNotIn('Cookie', entry['http_headers'])
        self.assertNotIn('Authorization', entry['http_headers'])
        self.assertEqual(PAGE, entry['http_headers']['Referer'])


if __name__ == '__main__':
    unittest.main()
