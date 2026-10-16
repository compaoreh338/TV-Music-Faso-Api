using System.Globalization;
using System.Text;
using System.Xml.Linq;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class PlaylistExportService
{
    private readonly PlayoutPathMapper _paths;

    public PlaylistExportService(PlayoutPathMapper? paths = null)
    {
        _paths = paths ?? new PlayoutPathMapper();
    }

    public string ToCsv(DaySchedule schedule)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Date;Tranche;HeureDebut;Position;Artiste;Titre;Langue;Genre;Origine;Duree;Fichier");

        foreach (var (item, start) in Flatten(schedule))
        {
            builder.AppendLine(string.Join(';',
                (item.Playlist.Date == default ? schedule.Date : item.Playlist.Date).ToString("yyyy-MM-dd"),
                item.Playlist.Slot.ToDisplayName(),
                start.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                item.Item.Position,
                Quote(item.Item.Clip.Artist),
                Quote(item.Item.Clip.Title),
                item.Item.Clip.Language.ToDisplayName(),
                item.Item.Clip.Genre.ToDisplayName(),
                ExportText.Origin(item.Item.Clip.IsBurkinabe),
                item.Item.Clip.DurationLabel,
                FileOrFallback(item.Item.Clip)));
        }

        return builder.ToString();
    }

    public string ToM3u(DaySchedule schedule)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#EXTM3U");
        builder.AppendLine("#EXTENC:UTF-8");
        builder.AppendLine("# TV-Music Faso / OBS / serveurs de diffusion");

        foreach (var playlist in schedule.Playlists)
        {
            builder.AppendLine($"# TV-Music Faso - {playlist.Slot.ToDisplayName()}");
            foreach (var item in playlist.Items)
            {
                builder.AppendLine($"#EXTINF:{(int)item.Clip.Duration.TotalSeconds},{item.Clip.Artist} - {item.Clip.Title}");
                builder.AppendLine(FileOrFallback(item.Clip));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Playlist MovieJaySX (.mpl), calquée sur les fichiers enregistrés par MovieJay
    /// (racine m_config, 14 pistes, background, file Medialooks avec mitem_props).
    /// </summary>
    public string ToMovieJayMpl(DaySchedule schedule)
    {
        var playlistName = "TV-Music Faso " + schedule.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var builder = new StringBuilder();
        builder.Append("<m_config path='").Append(MovieJayAttr(playlistName + ".mpl")).AppendLine("'>");
        builder.AppendLine("\t<video aspect_ratio='16:9'/>");
        builder.AppendLine("\t<audio channels='14' rate='48000' bits='16' track_split='14'>");
        builder.AppendLine("\t\t<track source_index='0' mode='enabled' input_channels='0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13' gain='4.0, 4.0, 4.0, 4.0, 4.0, 4.0, 4.0, 4.0, 4.0, 4.0, 4.0, 4.0, 4.0, 4.0' mute='0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0' output_channels='0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13' desc='Track 0'/>");
        builder.AppendLine("\t</audio>");
        builder.Append(MovieJayBackground);

        var index = 0;
        foreach (var (item, _) in Flatten(schedule))
        {
            AppendMovieJayFile(builder, item.Item.Clip, playlistName, index++);
        }

        builder.AppendLine("</m_config>");
        return builder.ToString();
    }

    private void AppendMovieJayFile(StringBuilder builder, Clip clip, string playlistName, int index)
    {
        var path = FileOrFallback(clip);
        var seconds = MovieJaySeconds(clip.Duration);
        var fileName = Path.GetFileName(path);
        var title = string.IsNullOrWhiteSpace(fileName) ? clip.Title : fileName;
        var artist = string.IsNullOrWhiteSpace(clip.Artist) ? "Programme" : clip.Artist;
        var frames = Math.Max(1, (int)Math.Round(Math.Max(0.1, clip.Duration.TotalSeconds) * 25d, MidpointRounding.AwayFromZero));
        var refId = $"0x{(0x30000000 + index):X8}";

        builder.Append("\t<file in='0.0' out='").Append(seconds)
            .Append("' path='").Append(MovieJayAttr(path))
            .Append("' interlace='default' aspect_ratio='' scale_type='default' external_audio='' external_audio_offset='' preview.pull='false' img_stub='' img.default_duration='86400' img.tone='' no_breaks='false' open_file.max_wait='10000' open_url.max_wait='15000' read_url.max_wait='3000' read_file.max_wait='3000' force_mpeg_tc='false' audio_track='all' video_track='0' subtitle_track='-1' ts_program='0' network.buffer_min='5.0' network.buffer_max='20.0' network.rate_control='true' file.buffer_min='0.0' file.buffer_max='2.0' decode='auto' decode.fallback='false' file.max_forward_rate='4.0' file.max_reverse_rate='2.0' out_point.mode='wait' mfreader.convert_format='true' output.10bit='false' mxf.use_track_origin='false' start_timecode='' duration='")
            .Append(seconds).Append("' ref_id='").Append(refId).AppendLine("' loop='false'>");
        builder.Append("\t\t<info ts_programs='0' subtitle_tracks='0' video_tracks='1' audio_tracks='1' streams='2' format='mov,mp4,m4a,3gp,3g2,mj2' format_name='QuickTime / MOV' start_time='0.0' duration='")
            .Append(seconds).Append("' size='0' bitrate='0' bitrate_kbps='0' play_while_rec='' splitter_name='ffmpeg'>").AppendLine();
        builder.AppendLine("\t\t\t<metadata major_brand='mp42' minor_version='0' compatible_brands='mp42mp41'/>");
        builder.Append("\t\t\t<video.0 idx='0' pid='1' codec='h264' codec_name='H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10' codec_tag='avc1' profile='High' width='1920' height='1080' has_b_frames='1' pix_fmt='0' attached_pic='false' pixel_ar='1:1' display_ar='16:9' r_frame_rate='25/1' avg_frame_rate='25/1' time_base='1/25000' duration='")
            .Append(seconds).Append("' nb_frames='").Append(frames).AppendLine("' frame_rate_correct='true'/>");
        builder.Append("\t\t\t<audio.0 idx='1' pid='2' codec='aac' codec_name='AAC (Advanced Audio Coding)' codec_tag='mp4a' profile='LC' format='fltp' sample_rate='48000' channels='2' bits='-32' time_base='1/48000' duration='")
            .Append(seconds).AppendLine("' frame_rate_correct='false'/>");
        builder.AppendLine("\t\t</info>");
        builder.AppendLine("\t\t<video video_format='HD1080-25p' width='1920' height='1080' rate='25.0' pixel_format='HDYC' aspect_ratio='16:9' interlace='progressive'/>");
        builder.AppendLine("\t\t<audio channels='2' rate='48000' bits='-32' track_split='2'>");
        builder.AppendLine("\t\t\t<track source_index='0' mode='enabled' input_channels='0, 1' gain='0.0, 0.0' mute='0, 0' output_channels='0, 1' desc='Track 0'/>");
        builder.AppendLine("\t\t</audio>");
        builder.AppendLine("\t\t<object imaudio.enabled='true' audio_channels='' audio_gain='' gpu.rgb_transform_matrix='' default_name='MFile' default_tracks='0' channels_per_track='0' internal.convert_frame='false' experimental.buffers='0' pause.fields='2' pause.use_reference='false' afilter='' scaling_quality='auto' scaling_deinterlace_first='false' crop='' mirror='' rotate='' overlay_rms='false' overlay_rms.pos='0.1' overlay_rms.color='green' overlay_waveform='false' overlay_waveform.pos='-0.3' overlay_waveform.color='' audio.variable_rate='true' audio.lufs='false' audio.lufs_state='' audio.lufs_channels='' audio.gain_normalization='false' gain_normalization.target_i='-23.0' gain_normalization.gating='-100.0' gain_normalization.loudness_type='rms' timecode.time_sync='0' timecode.offset='' mfconverter.enabled='true' format.set_default_pixelformat='true' mdelay.enabled='false' mdelay.live_preview='false' playback_rate='1.0' experimental.exact_duration='false'/>");
        builder.Append("\t\t<mitem_props stop_in='' stop_out='' pause_in='' pause_out='' schedule_waitstart='' ItemType='8' FadeIn='-1' FadeOut='0' IsInsert='2' RandomPath='")
            .Append(MovieJayAttr(path)).Append("' FileName='").Append(MovieJayAttr(path))
            .Append("' SongTitle='").Append(MovieJayAttr(title))
            .Append("' Artist='").Append(MovieJayAttr(artist))
            .Append("' PlayListName='").Append(MovieJayAttr(playlistName))
            .AppendLine("' Marker1='' Marker2='' Marker3='' Marker4='' Marker5='' PrgType='' PrgDetails='' Producer='' IsPlayed='0'>");
        builder.AppendLine("\t\t\t<transition_in type='' time=''/>");
        builder.AppendLine("\t\t\t<transition_out type='' time=''/>");
        builder.AppendLine("\t\t\t<file/>");
        builder.AppendLine("\t\t\t<device/>");
        builder.AppendLine("\t\t\t<object/>");
        builder.AppendLine("\t\t\t<audio/>");
        builder.AppendLine("\t\t</mitem_props>");
        builder.AppendLine("\t</file>");
    }

    private const string MovieJayBackground =
        """
        	<background>
        		<file in='0.0' path='black' start_timecode='' interlace='' aspect_ratio='' scale_type='default' external_audio='' external_audio_offset='' preview.pull='false' img_stub='' img.default_duration='86400' img.tone='' no_breaks='false' open_file.max_wait='10000' open_url.max_wait='15000' read_url.max_wait='3000' read_file.max_wait='3000' force_mpeg_tc='false' audio_track='0' video_track='0' subtitle_track='-1' ts_program='0' network.buffer_min='5.0' network.buffer_max='20.0' network.rate_control='true' file.buffer_min='0.0' file.buffer_max='2.0' decode='auto' decode.fallback='false' file.max_forward_rate='4.0' file.max_reverse_rate='2.0' out_point.mode='wait' mfreader.convert_format='true' output.10bit='false' mxf.use_track_origin='false' background='true' duration='86400.0' loop='true' ref_id='0x2B9BF9E8'>
        			<info/>
        			<video video_format='NTSC-16x9' width='720' height='-486' rate='29.97003' pixel_format='RGB32' aspect_ratio='40:27' interlace='bottom'/>
        			<audio channels='14' rate='48000' bits='16' track_split='14'>
        				<track source_index='0' mode='enabled' input_channels='0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13' gain='0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0' mute='0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0' output_channels='0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13' desc='Track 0'/>
        			</audio>
        			<object imaudio.enabled='true' audio_channels='' audio_gain='' gpu.rgb_transform_matrix='' default_name='MFile' default_tracks='0' channels_per_track='0' internal.convert_frame='false' experimental.buffers='0' pause.fields='2' pause.use_reference='false' afilter='' scaling_quality='auto' scaling_deinterlace_first='false' crop='' mirror='' rotate='' overlay_rms='false' overlay_rms.pos='0.1' overlay_rms.color='green' overlay_waveform='false' overlay_waveform.pos='-0.3' overlay_waveform.color='' audio.variable_rate='true' audio.lufs='false' audio.lufs_state='' audio.lufs_channels='' audio.gain_normalization='false' gain_normalization.target_i='-23.0' gain_normalization.gating='-100.0' gain_normalization.loudness_type='rms' timecode.time_sync='0' timecode.offset='' mfconverter.enabled='true' format.set_default_pixelformat='true' mdelay.enabled='false' mdelay.live_preview='false' playback_rate='1.0' background='true' experimental.exact_duration='false'/>
        			<mitem_props stop_in='' stop_out='' pause_in='' pause_out='' schedule_waitstart=''>
        				<transition_in type='' time=''/>
        				<transition_out type='' time=''/>
        				<file/>
        				<device/>
        				<object/>
        				<audio/>
        			</mitem_props>
        		</file>
        	</background>

        """;

    private static string MovieJaySeconds(TimeSpan duration)
    {
        var seconds = Math.Max(0.1, duration.TotalSeconds);
        return seconds.ToString("0.0#####", CultureInfo.InvariantCulture);
    }

    private static string MovieJayAttr(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);

    /// <summary>
    /// Liste d'entrées vidéo pour import vMix.
    /// </summary>
    public string ToVmixXml(DaySchedule schedule)
    {
        var inputs = Flatten(schedule)
            .Select((entry, i) => new XElement("input",
                new XAttribute("number", i + 1),
                new XAttribute("type", "Video"),
                new XAttribute("title", $"{entry.Item.Item.Clip.Artist} - {entry.Item.Item.Clip.Title}"),
                new XAttribute("duration", (int)entry.Item.Item.Clip.Duration.TotalMilliseconds),
                FileOrFallback(entry.Item.Item.Clip)))
            .ToArray();

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("vmix",
                new XAttribute("channel", "TV-Music Faso"),
                new XAttribute("date", schedule.Date.ToString("yyyy-MM-dd")),
                new XElement("inputs", inputs)));

        return document.ToString();
    }

    private static IEnumerable<(FlattenedItem Item, TimeOnly Start)> Flatten(DaySchedule schedule)
    {
        foreach (var playlist in schedule.Playlists)
        {
            var cursor = playlist.SlotStart;
            foreach (var item in playlist.Items)
            {
                yield return (new FlattenedItem(playlist, item), cursor);
                cursor = cursor.Add(item.Clip.Duration);
            }
        }
    }

    private string FileOrFallback(Clip clip) =>
        _paths.Map(clip.FilePath, $"{clip.Artist} - {clip.Title}.mp4");

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private readonly record struct FlattenedItem(Playlist Playlist, PlaylistItem Item);
}
