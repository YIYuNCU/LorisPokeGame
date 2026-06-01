// -----------------------------------------------------------------------
// CardImageProvider.cs - 扑克牌图片预加载与缓存
// 支持兜底：图片缺失时程序化绘制牌面
// -----------------------------------------------------------------------

using System.IO;
using System.Windows.Media.Imaging;
using LolitaPoker.Core.Models;

namespace LolitaPoker.Core.Assets;

/// <summary>
/// 扑克牌图片提供器 - 预加载并缓存所有卡牌图片
/// </summary>
public static class CardImageProvider
{
    private static readonly Dictionary<string, BitmapImage> _cache = new();
    private static readonly Dictionary<Card, BitmapImage> _cardCache = new();
    private static BitmapImage? _cardBack;
    private static bool _initialized;

    /// <summary>
    /// 初始化：加载 pics/ 目录下所有图片到内存缓存
    /// </summary>
    public static void Initialize(string basePath)
    {
        if (_initialized) return;

        if (Directory.Exists(basePath))
        {
            foreach (var file in Directory.GetFiles(basePath, "*.png"))
            {
                var fileName = Path.GetFileName(file);
                var image = LoadImage(file);
                if (image != null)
                    _cache[fileName] = image;
            }
        }

        // 背景.png 用作牌背（不是游戏背景）
        if (_cache.TryGetValue("背景.png", out var backImg))
        {
            _cardBack = backImg;
        }
        else
        {
            _cardBack = FallbackCardRenderer.RenderCardBack();
        }

        _initialized = true;
    }

    /// <summary>
    /// 根据牌获取对应的图片（优先缓存 → 读文件 → 兜底绘制）
    /// </summary>
    public static BitmapImage GetCardImage(Card card)
    {
        // 已缓存
        if (_cardCache.TryGetValue(card, out var cached))
            return cached;

        var fileName = CardHelper.GetImageFileName(card);

        // 从预加载缓存取
        if (_cache.TryGetValue(fileName, out var image))
        {
            _cardCache[card] = image;
            return image;
        }

        // 兜底：程序化绘制牌面
        var fallback = FallbackCardRenderer.RenderCardFace(card);
        _cardCache[card] = fallback;
        return fallback;
    }

    /// <summary>
    /// 获取牌背图片
    /// </summary>
    public static BitmapImage GetCardBack() => _cardBack!;

    /// <summary>
    /// 检查是否已初始化
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// 安全加载图片
    /// </summary>
    private static BitmapImage? LoadImage(string filePath)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(filePath, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 200; // 限制解码宽度，节省内存
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null; // 加载失败时返回null，后续走兜底
        }
    }
}
