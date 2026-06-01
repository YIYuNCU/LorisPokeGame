// -----------------------------------------------------------------------
// FallbackCardRenderer.cs - 无图片时程序化绘制牌面
// -----------------------------------------------------------------------

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Models;

namespace LolitaPoker.Core.Assets;

/// <summary>
/// 当图片缓存缺失时，程序化绘制扑克牌面
/// </summary>
public static class FallbackCardRenderer
{
    private static readonly Typeface Font = new("Segoe UI");
    private static readonly Typeface SymbolFont = new("Segoe UI Symbol");

    private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(200, 30, 30));
    private static readonly Brush BlackBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private static readonly Brush WhiteBrush = Brushes.White;
    private static readonly Brush CardBgBrush = new SolidColorBrush(Color.FromRgb(250, 248, 240));
    private static readonly Brush BackBrush = new SolidColorBrush(Color.FromRgb(44, 62, 80));
    private static readonly Brush BackBorderBrush = new SolidColorBrush(Color.FromRgb(74, 111, 165));

    static FallbackCardRenderer()
    {
        RedBrush.Freeze();
        BlackBrush.Freeze();
        CardBgBrush.Freeze();
        BackBrush.Freeze();
        BackBorderBrush.Freeze();
    }

    /// <summary>
    /// 程序化绘制一张牌的正面图像
    /// </summary>
    public static BitmapImage RenderCardFace(Card card)
    {
        int w = 140, h = 200;
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            // 牌面背景
            dc.DrawRoundedRectangle(CardBgBrush, new Pen(Brushes.Gray, 1.5),
                new Rect(0, 0, w, h), 8, 8);

            bool isRed = card.Suit == Suit.Diamonds || card.Suit == Suit.Hearts;
            var colorBrush = isRed ? RedBrush : BlackBrush;
            string suitSymbol = GetSuitSymbol(card.Suit);
            string rankText = GetRankText(card.Rank);

            // 大小王特殊处理
            if (card.IsJoker)
            {
                bool isBig = card.Rank == Rank.BigJoker;
                var jokerColor = isBig ? RedBrush : BlackBrush;
                string jokerText = isBig ? "大\n王" : "小\n王";

                var ft = new FormattedText(jokerText, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Font, 28, jokerColor, 1.2);
                dc.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));

                // 角标
                var corner = new FormattedText("★", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, SymbolFont, 14, jokerColor, 1.2);
                dc.DrawText(corner, new Point(6, 4));
            }
            else
            {
                // 左上角：点数 + 花色
                var rankFt = new FormattedText(rankText, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Font, 20, colorBrush, 1.2);
                dc.DrawText(rankFt, new Point(6, 4));

                var suitFt = new FormattedText(suitSymbol, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, SymbolFont, 16, colorBrush, 1.2);
                dc.DrawText(suitFt, new Point(8, 26));

                // 右下角（倒转）
                dc.PushTransform(new RotateTransform(180, w / 2.0, h / 2.0));
                dc.DrawText(rankFt, new Point(w - rankFt.Width - 6, 4));
                dc.DrawText(suitFt, new Point(w - suitFt.Width - 8, 26));
                dc.Pop();

                // 中央大花色
                var centerFt = new FormattedText(suitSymbol, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, SymbolFont, 52, colorBrush, 1.2);
                dc.DrawText(centerFt, new Point((w - centerFt.Width) / 2, (h - centerFt.Height) / 2));
            }
        }

        return VisualToBitmap(visual, w, h);
    }

    /// <summary>
    /// 程序化绘制牌背图像（当背景.png缺失时使用）
    /// </summary>
    public static BitmapImage RenderCardBack()
    {
        int w = 140, h = 200;
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            // 深色背景
            dc.DrawRoundedRectangle(BackBrush, new Pen(BackBorderBrush, 2),
                new Rect(0, 0, w, h), 8, 8);

            // 内框
            dc.DrawRoundedRectangle(null, new Pen(BackBorderBrush, 1),
                new Rect(8, 8, w - 16, h - 16), 4, 4);

            // 装饰花纹（交叉斜线）
            var patternPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
            for (int i = -h; i < w + h; i += 12)
            {
                dc.DrawLine(patternPen, new Point(i, 0), new Point(i + h, h));
                dc.DrawLine(patternPen, new Point(i + h, 0), new Point(i, h));
            }

            // 中央菱形
            var centerBrush = new SolidColorBrush(Color.FromArgb(60, 74, 111, 165));
            centerBrush.Freeze();
            var diamond = new StreamGeometry();
            using (var ctx = diamond.Open())
            {
                ctx.BeginFigure(new Point(w / 2.0, h / 2.0 - 25), true, true);
                ctx.LineTo(new Point(w / 2.0 + 18, h / 2.0), true, false);
                ctx.LineTo(new Point(w / 2.0, h / 2.0 + 25), true, false);
                ctx.LineTo(new Point(w / 2.0 - 18, h / 2.0), true, false);
            }
            dc.DrawGeometry(centerBrush, new Pen(BackBorderBrush, 1.5), diamond);
        }

        return VisualToBitmap(visual, w, h);
    }

    private static string GetSuitSymbol(Suit suit) => suit switch
    {
        Suit.Spades => "♠",
        Suit.Hearts => "♥",
        Suit.Diamonds => "♦",
        Suit.Clubs => "♣",
        _ => "★"
    };

    private static string GetRankText(Rank rank) => rank switch
    {
        Rank.Three => "3",
        Rank.Four => "4",
        Rank.Five => "5",
        Rank.Six => "6",
        Rank.Seven => "7",
        Rank.Eight => "8",
        Rank.Nine => "9",
        Rank.Ten => "10",
        Rank.Jack => "J",
        Rank.Queen => "Q",
        Rank.King => "K",
        Rank.Ace => "A",
        Rank.Two => "2",
        _ => "?"
    };

    private static BitmapImage VisualToBitmap(DrawingVisual visual, int w, int h)
    {
        var bmp = new RenderTargetBitmap(w * 2, h * 2, 192, 192, PixelFormats.Pbgra32);
        bmp.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));

        using var ms = new System.IO.MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
