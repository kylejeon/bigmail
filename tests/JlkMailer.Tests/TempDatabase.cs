using JlkMailer.Infrastructure.Storage;
using Xunit;

namespace JlkMailer.Tests;

/// <summary>
/// 테스트용 임시 SQLite 파일. Dispose 시 파일이 실제로 지워지는지까지 확인한다.
///
/// Windows 는 열린 핸들이 남아 있으면 파일 삭제가 실패하지만 Linux/macOS 는 unlink 가 성공한다.
/// 그래서 '핸들을 놓지 않는 버그'가 macOS 에서는 드러나지 않는다.
/// 여기서 삭제를 강제로 확인해 두면 양쪽에서 같은 신호가 난다.
/// </summary>
public sealed class TempDatabase : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"jlk-test-{Guid.NewGuid():N}.db");

    public string WalPath => Path + "-wal";
    public string ShmPath => Path + "-shm";

    public SqliteCampaignStore CreateStore()
    {
        var store = new SqliteCampaignStore(Path);
        store.Initialize();
        return store;
    }

    public void Dispose()
    {
        // 백신·인덱서가 잠깐 파일을 잡는 경우가 있어 몇 번 재시도한다.
        foreach (var suffix in new[] { "-wal", "-shm", "" })
        {
            var file = Path + suffix;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (!File.Exists(file)) break;
                try { File.Delete(file); break; }
                catch (IOException) { Thread.Sleep(50); }
                catch (UnauthorizedAccessException) { Thread.Sleep(50); }
            }
        }
    }
}

public class SqliteFileHandleTests
{
    /// <summary>
    /// 회귀 방지: Microsoft.Data.Sqlite 는 기본이 커넥션 풀링이라
    /// Close() 만으로는 파일 핸들이 풀려나지 않는다.
    /// 그러면 Windows 에서 campaign.db 를 지우거나 옮길 수 없고, DB 경로 변경도 실패한다.
    /// </summary>
    [Fact]
    public void Dispose_하면_WAL_파일이_정리되고_DB_파일을_지울_수_있다()
    {
        using var temp = new TempDatabase();

        var store = temp.CreateStore();
        store.UpsertCampaign(new Core.Models.Campaign { Name = "잠금 테스트" });

        Assert.True(File.Exists(temp.Path));

        store.Dispose();

        // 커넥션이 진짜로 닫혔다면 SQLite 가 체크포인트 후 WAL 파일을 지운다.
        // 핸들이 풀에 남아 있으면 이 파일들이 그대로 있다.
        Assert.False(File.Exists(temp.WalPath), "-wal 이 남아 있습니다. 커넥션이 닫히지 않았습니다.");
        Assert.False(File.Exists(temp.ShmPath), "-shm 이 남아 있습니다. 커넥션이 닫히지 않았습니다.");

        // Windows 에서는 핸들이 남아 있으면 여기서 IOException 이 난다.
        File.Delete(temp.Path);
        Assert.False(File.Exists(temp.Path));
    }

    /// <summary>같은 파일을 닫았다 다시 열 수 있어야 한다. 앱에서 DB 경로를 바꿀 때의 경로다.</summary>
    [Fact]
    public void 닫은_뒤_같은_파일을_다시_열_수_있다()
    {
        using var temp = new TempDatabase();

        var first = temp.CreateStore();
        var id = first.UpsertCampaign(new Core.Models.Campaign { Name = "첫 번째" });
        first.Dispose();

        using var second = temp.CreateStore();
        Assert.Equal("첫 번째", second.GetCampaign(id)!.Name);
    }
}
