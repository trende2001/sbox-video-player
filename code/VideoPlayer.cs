using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Editor;
using Sandbox;
using MediaHelpers;

[HammerEntity]
[Spawnable]
[Library("ent_videoplayer", Title = "Video Player")]
[Description("Custom, resizable video player panel. No controls added on top.")]
public partial class VideoPlayerEntity : ModelEntity
{
    private static VideoPlayer Video;

    internal Texture VideoTexture => Video.Texture;
    
    [Net, Property("video_url", "Video URL")]
    private string VideoURL { get; set; }
    
    [Net, Property("repeat_video", "Loop")]
    private bool Loop { get; set; }
    
    [Net, Property("video_height", "Video Height")]
    private float VideoHeight { get; set; } = 1920;
    
    [Net, Property("video_width", "Video Width")]
    private float VideoWidth { get; set; } = 1080;

    [Net, Property("video_volume", "Volume")]
    private float VideoVolume { get; set; } = 90;

    [Net, Property("video_opacity", "Opacity")]
    [MinMax(0f, 1f)]
    private float VideoOpacity { get; set; } = 1f;
    
    protected bool VideoLoaded { get; set; }
    protected bool AudioLoaded { get; set; }
    protected bool VideoHasHitched => VideoLoaded && VideoLastUpdated > 1f;
    protected bool IsInitializing { get; set; }
    protected TimeSince VideoLastUpdated { get; set; }   
    
    public override void Spawn()
    {
        base.Spawn();
        
        Tags.Add("solid");

        Position = new Vector3(Position);
        
        UsePhysicsCollision = true;

        SetupPhysicsFromModel(PhysicsMotionType.Static);
    }

    public override void ClientSpawn()
    {
        base.ClientSpawn();

        PlayVideo(VideoURL);
    }
    
    [GameEvent.Client.Frame]
    private void ClientFrame()
    {
        if (Video == null)
            return;
        
        Video?.Present();
        
        if ( SceneObject.IsValid() && Video.Texture.IsLoaded )
        {
            SceneObject.Attributes.Set( "texture", Video.Texture );
            SceneObject.Attributes.Set("opacity", VideoOpacity);
        }

        if (VideoHasHitched)
        {
            FixVideoHitch();
        }
    }


    public static void DrawGizmos(EditorContext context)
    {
        var video_width = context.Target.GetProperty("video_width");
        var video_height = context.Target.GetProperty("video_height");
        Vector2 videosize = new Vector2(video_height.GetValue<float>(), video_width.GetValue<float>());

        Vector3 BoxMins = new Vector3(10f, -videosize.x * 0.5f, -videosize.y * 0.5f) * 0.25f;
        Vector3 BoxMaxs = new Vector3(10f, videosize.x * 0.5f, videosize.y * 0.5f) * 0.25f;
        

        Gizmo.Draw.Color = Color.Yellow.WithAlpha(0.8f);
        Gizmo.Draw.LineThickness = 5f;
        Gizmo.Draw.LineBBox(new BBox(BoxMins, BoxMaxs));
        Gizmo.Draw.SolidBox(new BBox(BoxMins, BoxMaxs));
        Gizmo.Draw.Model("models/arrow.vmdl", new Transform(new Vector3(-40f, 0f, 0f),  Rotation.LookAt(Vector3.Down)));
        
        using (Gizmo.Scope())
        {
            if (Gizmo.Settings.EditMode != "select")
                return;
            
            if (!context.IsSelected)
                return;

            double WidthArrowGirth = 15f + videosize.x / 2;
            double HeightArrowGirth = 15f + videosize.y / 2;

            // This is garbage

            Gizmo.Draw.Color = Gizmo.Colors.Up;

            if (Gizmo.Control.Arrow("height_arrow1", Vector3.Up * 0.5f * 0.25f,
                    out var local1, 25f, (float)HeightArrowGirth, videosize.y, head: "cone"))
                videosize.y += local1;
            if (Gizmo.Control.Arrow("height_arrow2", Vector3.Down * 0.5f * 0.25f,
                    out var local2, 25f, (float)HeightArrowGirth, videosize.y, head: "cone"))
                videosize.y += local2;

            Gizmo.Draw.Color = Gizmo.Colors.Left;
            if (Gizmo.Control.Arrow("width_arrow1", Vector3.Right * 0.5f * 0.25f,
                    out var local3, 25f, (float)WidthArrowGirth, videosize.x, head: "cone"))
                videosize.x += local3;
            if (Gizmo.Control.Arrow("width_arrow2", Vector3.Left * 0.5f * 0.25f,
                    out var local4, 25f, (float)WidthArrowGirth, videosize.x, head: "cone"))
                videosize.x += local4;

            video_width.SetValue<float>(Math.Max(videosize.y, 0.1f));
            video_height.SetValue<float>(Math.Max(videosize.x, 0.1f));
        }
    }

    public async Task PlayVideo(string url)
    {
        IsInitializing = true;
        
            Video = new VideoPlayer();
            Video.OnAudioReady = () =>
            {
                var soundHandle = Video.PlayAudio(this);
                soundHandle.Volume = VideoVolume;
                AudioLoaded = true;
            };
            Video.Repeat = Loop;
            Video.OnLoaded = OnLoaded;

            if(MediaHelper.IsYoutubeUrl(url))
            {
                YoutubePlayerResponse youtube = await MediaHelper.GetYoutubePlayerResponseFromUrl(url);
                string streamUrl = youtube.GetStreamUrl();
                Log.Info(streamUrl);
                Video.Play(streamUrl);
            }
            else
            {
                // TODO: Make it search for mounted stuff later down the road too
                Video.Play(url);
            }

            await WaitUntilReady();
    }

    [ClientRpc]
    public async void PlayVideoRpc()
    {
        await PlayVideo(VideoURL);
    }
    private void OnLoaded()
    {
        var size = new Vector2( VideoHeight, VideoWidth );
        Model = Model.Builder
            .AddMesh( CreatePlane( size * 0.25f ) )
            .Create();

        if ( SceneObject.IsValid() )
        {
            SceneObject.Batchable = false;
            SceneObject.Attributes.Set( "texture", Video.Texture );
            SceneObject.Attributes.Set("opacity", VideoOpacity);
        }
    }

    private static Mesh CreatePlane( Vector2 size )
    {
        var halfWidth = size.x * 0.5f;
        var halfHeight = size.y * 0.5f;
		
        var material = Material.Load( "materials/screen.vmat" );
        var mesh = new Mesh( material );
		
		// Create plane with 4 vertices
        mesh.CreateVertexBuffer<Vertex>( 4, Vertex.Layout, new[]
        {
            new Vertex( new Vector3( 0, -halfWidth, -halfHeight ), Vector3.Backward, Vector3.Up, new Vector4( 1, 1, 0, 0 ) ),
            new Vertex( new Vector3( 0, halfWidth, -halfHeight ), Vector3.Backward, Vector3.Up, new Vector4( 0, 1, 0, 0 ) ),
            new Vertex( new Vector3( 0, halfWidth, halfHeight ), Vector3.Backward, Vector3.Up, new Vector4( 0, 0, 0, 0 ) ),
            new Vertex( new Vector3( 0, -halfWidth, halfHeight ), Vector3.Backward, Vector3.Up, new Vector4( 1, 0, 0, 0 ) ),
        } );
		
		// The array contains six indices, which determine the order in which vertices should be connected to form 2 triangles
        mesh.CreateIndexBuffer( 6, new[] { 2, 1, 0, 0, 3, 2 } );
		
        var bounds = new Vector3( 10, size.x, size.y );
        mesh.Bounds = new BBox( -bounds, bounds );

        return mesh;
    }

    protected virtual void FixVideoHitch()
    {
        Log.Warning("Video hitch detected, seeking to current time");
        VideoLoaded = false;
        Refresh();
    }

    public virtual void Seek(float time)
    {
        if (!VideoLoaded)
            return;
    
        // Set the video loaded flag to false, VideoHasHitched will detect
        // that it had happened.
        VideoLoaded = false;
        Video?.Seek(time);
    }
    

    protected virtual async Task WaitUntilReady()
    {
        if (!IsInitializing)
            return;

        while (!(VideoLoaded))
        {
            await GameTask.DelaySeconds(Time.Delta);
        }

        IsInitializing = false;
        Seek(0f);
    }
    
    protected virtual void Refresh()
    {
        IsInitializing = false;
        var CurrentTime = Video.PlaybackTime;
        PlayVideo(VideoURL);
        GameTask.RunInThreadAsync(async () =>
        {
            await WaitUntilReady();
            Seek(CurrentTime);
        });
    }
    
    
    protected override void OnDestroy()
    {
        base.OnDestroy();

        Video?.Dispose();
        Video = null;
    }
}