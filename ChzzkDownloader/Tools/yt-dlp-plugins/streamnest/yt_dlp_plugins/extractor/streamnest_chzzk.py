import re
import urllib.parse

from yt_dlp.extractor.chzzk import CHZZKVideoIE
from yt_dlp.utils import float_or_none, int_or_none, mimetype2ext, parse_codecs, update_url_query


class _StreamNestCHZZKVideoIE(CHZZKVideoIE, plugin_name='streamnest'):
    """Support CHZZK single-file DASH manifests that use byte ranges."""

    IE_NAME = 'chzzk:video:streamnest'

    def _extract_mpd_formats_and_subtitles(
            self, mpd_url, video_id, mpd_id=None, note=None, errnote=None,
            fatal=True, data=None, headers={}, query={}):
        try:
            return super()._extract_mpd_formats_and_subtitles(
                mpd_url, video_id, mpd_id=mpd_id, note=note, errnote=errnote,
                fatal=fatal, data=data, headers=headers, query=query)
        except KeyError as error:
            if error.args != ('sourceURL',):
                raise

        formats, subtitles = self._extract_range_mpd_formats(
            mpd_url, video_id, mpd_id=mpd_id, fatal=fatal,
            data=data, headers=headers, query=query)
        if formats:
            self.write_debug(
                f'CHZZK range-based DASH manifest parsed into {len(formats)} formats')
            return formats, subtitles

        # Last-resort compatibility path. FFmpeg can read the complete MPD, but
        # it chooses the quality automatically because no individual formats
        # could be reconstructed.
        manifest_url = update_url_query(mpd_url, query)
        self.report_warning(
            'CHZZK DASH manifest omitted Initialization@sourceURL; '
            'using the StreamNest FFmpeg automatic-quality fallback')
        return ([{
            'format_id': 'dash-ffmpeg-auto',
            'format_note': 'DASH (FFmpeg automatic quality)',
            'url': manifest_url,
            'manifest_url': manifest_url,
            'ext': 'mp4',
            'protocol': 'm3u8',
            'vcodec': 'unknown',
            'acodec': 'unknown',
            'http_headers': headers,
        }], {})

    def _extract_range_mpd_formats(
            self, mpd_url, video_id, mpd_id=None, fatal=True,
            data=None, headers={}, query={}):
        response = self._download_xml_handle(
            mpd_url, video_id,
            note='Downloading range-based MPD manifest',
            errnote='Failed to download range-based MPD manifest',
            fatal=fatal, data=data, headers=headers, query=query)
        if not response:
            return [], {}

        mpd_doc, url_handle = response
        if mpd_doc is None:
            return [], {}

        manifest_url = url_handle.url
        namespace_match = re.match(r'(?i)^\{([^}]+)\}MPD$', mpd_doc.tag)
        namespace = namespace_match.group(1) if namespace_match else None

        def ns(tag):
            return f'{{{namespace}}}{tag}' if namespace else tag

        def resolve_base(parent_url, element):
            base_element = element.find(ns('BaseURL'))
            text = base_element.text.strip() if base_element is not None and base_element.text else ''
            return urllib.parse.urljoin(parent_url, text) if text else parent_url

        def nearest_segment_list(*elements):
            for element in elements:
                segment_list = element.find(ns('SegmentList'))
                if segment_list is not None:
                    return segment_list
            return None

        root_base = resolve_base(manifest_url, mpd_doc)
        range_formats = []
        stream_number = 0

        for period in mpd_doc.findall(ns('Period')):
            period_base = resolve_base(root_base, period)
            for adaptation_set in period.findall(ns('AdaptationSet')):
                adaptation_base = resolve_base(period_base, adaptation_set)
                for representation in adaptation_set.findall(ns('Representation')):
                    representation_base = resolve_base(adaptation_base, representation)
                    segment_list = nearest_segment_list(
                        representation, adaptation_set, period, mpd_doc)
                    if segment_list is None or not self._is_range_only_segment_list(
                            segment_list, ns):
                        continue

                    fragments = self._range_fragments(
                        segment_list, representation_base, ns)
                    if not fragments:
                        continue

                    attributes = dict(adaptation_set.attrib)
                    attributes.update(representation.attrib)
                    mime_type = attributes.get('mimeType', '')
                    content_type = attributes.get('contentType') or mime_type.partition('/')[0]
                    if content_type not in ('video', 'audio'):
                        continue

                    codecs = parse_codecs(attributes.get('codecs', ''))
                    if content_type == 'video':
                        codecs.setdefault('acodec', 'none')
                    else:
                        codecs.setdefault('vcodec', 'none')

                    representation_id = attributes.get('id') or f'{content_type}-{stream_number}'
                    format_id = f'{mpd_id}-{representation_id}' if mpd_id else representation_id
                    ext = mimetype2ext(mime_type) or ('mp4' if content_type == 'video' else 'm4a')
                    byte_count = sum(
                        fragment['byte_range']['end'] - fragment['byte_range']['start']
                        for fragment in fragments if 'byte_range' in fragment)
                    has_drm = (
                        adaptation_set.find(ns('ContentProtection')) is not None
                        or representation.find(ns('ContentProtection')) is not None)

                    format_info = {
                        'format_id': format_id,
                        'manifest_url': manifest_url,
                        'url': manifest_url,
                        'fragment_base_url': urllib.parse.urljoin(representation_base, '.'),
                        'fragments': fragments,
                        'protocol': 'http_dash_segments',
                        'ext': ext,
                        'container': f'{ext}_dash',
                        'format_note': f'DASH {content_type} (byte ranges)',
                        'width': int_or_none(attributes.get('width')),
                        'height': int_or_none(attributes.get('height')),
                        'fps': self._parse_frame_rate(attributes.get('frameRate')),
                        'tbr': float_or_none(attributes.get('bandwidth'), scale=1000),
                        'asr': int_or_none(attributes.get('audioSamplingRate')),
                        'filesize': byte_count if byte_count and all('byte_range' in f for f in fragments) else None,
                        'language': attributes.get('lang'),
                        'http_headers': headers,
                        'manifest_stream_number': stream_number,
                        **codecs,
                    }
                    if has_drm:
                        format_info['has_drm'] = True
                    range_formats.append(format_info)
                    stream_number += 1

        # The CHZZK manifest is mixed: its video representations use ordinary
        # DASH templates while an audio representation uses single-file byte
        # ranges. Remove only the range-only representations from this copy,
        # let yt-dlp parse everything else, then merge both result sets.
        for period in mpd_doc.findall(ns('Period')):
            for adaptation_set in period.findall(ns('AdaptationSet')):
                range_representations = []
                for representation in adaptation_set.findall(ns('Representation')):
                    segment_list = nearest_segment_list(
                        representation, adaptation_set, period, mpd_doc)
                    if segment_list is not None and self._is_range_only_segment_list(
                            segment_list, ns):
                        range_representations.append(representation)
                for representation in range_representations:
                    adaptation_set.remove(representation)

        standard_formats, subtitles = self._parse_mpd_formats_and_subtitles(
            mpd_doc, mpd_id=mpd_id,
            mpd_base_url=urllib.parse.urljoin(manifest_url, '.'),
            mpd_url=manifest_url)
        for format_info in standard_formats:
            # CHZZK exposes the same quality both as one throttled progressive
            # file and as thousands of independent DASH fragments. Prefer the
            # fragmented rendition so the app's concurrent-fragments setting
            # can actually improve throughput.
            if format_info.get('protocol') == 'http_dash_segments':
                format_info['preference'] = 10
                format_info['source_preference'] = 10
            elif format_info.get('protocol') in ('http', 'https'):
                format_info['preference'] = -10
                format_info['source_preference'] = -10
        return standard_formats + range_formats, subtitles

    @staticmethod
    def _is_range_only_segment_list(segment_list, ns):
        initialization = segment_list.find(ns('Initialization'))
        if (initialization is not None
                and initialization.get('range')
                and not initialization.get('sourceURL')):
            return True
        return any(
            segment.get('mediaRange') and not segment.get('media')
            for segment in segment_list.findall(ns('SegmentURL')))

    @staticmethod
    def _range_fragments(segment_list, representation_base, ns):
        fragments = []
        initialization = segment_list.find(ns('Initialization'))
        if initialization is not None:
            byte_range = _StreamNestCHZZKVideoIE._parse_byte_range(
                initialization.get('range'))
            source_url = initialization.get('sourceURL')
            if byte_range:
                fragments.append({
                    'url': urllib.parse.urljoin(
                        representation_base, source_url) if source_url else representation_base,
                    'byte_range': byte_range,
                })
            elif source_url:
                fragments.append({'url': urllib.parse.urljoin(representation_base, source_url)})

        for segment in segment_list.findall(ns('SegmentURL')):
            byte_range = _StreamNestCHZZKVideoIE._parse_byte_range(
                segment.get('mediaRange'))
            media_url = segment.get('media')
            if byte_range:
                fragments.append({
                    'url': urllib.parse.urljoin(
                        representation_base, media_url) if media_url else representation_base,
                    'byte_range': byte_range,
                })
            elif media_url:
                fragments.append({'url': urllib.parse.urljoin(representation_base, media_url)})

        return fragments

    @staticmethod
    def _parse_byte_range(value):
        match = re.fullmatch(r'\s*(\d+)\s*-\s*(\d+)\s*', value or '')
        if not match:
            return None
        start, inclusive_end = map(int, match.groups())
        if inclusive_end < start:
            return None
        return {'start': start, 'end': inclusive_end + 1}

    @staticmethod
    def _parse_frame_rate(value):
        if not value:
            return None
        numerator, separator, denominator = value.partition('/')
        if separator:
            numerator_value = float_or_none(numerator)
            denominator_value = float_or_none(denominator)
            return numerator_value / denominator_value if numerator_value is not None and denominator_value else None
        return float_or_none(value)
