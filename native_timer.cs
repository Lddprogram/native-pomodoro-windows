using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class TomatoTimerForm : Form
{
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDC);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize, IntPtr hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int flags);

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; public NativePoint(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width; public int Height; public NativeSize(int width, int height) { Width = width; Height = height; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BlendFunction { public byte Op; public byte Flags; public byte Alpha; public byte Format; }

    private readonly Timer timer = new Timer();
    private readonly ContextMenuStrip menu = new ContextMenuStrip();
    private readonly Dictionary<string, double> stats = new Dictionary<string, double>();
    private readonly string dataDir;
    private readonly string settingsPath;
    private readonly string statsPath;
    private Image tomato;
    private Bitmap tomatoCutout;
    private Bitmap cachedBase;
    private Bitmap cachedTomato;
    private Bitmap frameBitmap;
    private int[] basePixels;
    private int[] tomatoPixels;
    private int[] breakPixels;
    private int[] framePixels;
    private float[] angleMap;
    private float[] featherMap;
    private bool running;
    private bool focusPhase = true;
    private bool infinite;
    private bool alwaysOnTop;
    private bool mouseMoved;
    private bool showMenuDots;
    private bool preset50 = true;
    private int completed;
    private int repetitions = 4;
    private int stateVersion;
    private int windowSize = 360;
    private double remaining;
    private double total;
    private double unsaved;
    private DateTime lastTick;
    private Point mouseDown;
    private Rectangle menuHit;
    private ToolStripMenuItem startItem;
    private ToolStripMenuItem statusItem;
    private ToolStripMenuItem statsItem;
    private ToolStripMenuItem topMostItem;
    private ToolStripMenuItem rhythmMenu;
    private ToolStripMenuItem rhythm50Item;
    private ToolStripMenuItem rhythm25Item;
    private ToolStripMenuItem countMenu;
    private ToolStripMenuItem infiniteItem;
    private ToolStripMenuItem sizeMenu;
    private readonly Dictionary<int, ToolStripMenuItem> countItems = new Dictionary<int, ToolStripMenuItem>();
    private readonly Dictionary<int, ToolStripMenuItem> sizeItems = new Dictionary<int, ToolStripMenuItem>();

    public TomatoTimerForm()
    {
        dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NativeTomatoTimer");
        settingsPath = Path.Combine(dataDir, "settings.txt");
        statsPath = Path.Combine(dataDir, "stats.txt");
        Directory.CreateDirectory(dataDir);
        LoadState();
        LoadTomato();

        Text = Assembly.GetExecutingAssembly().GetName().Name;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        TopMost = alwaysOnTop;
        BackColor = Color.White;
        ClientSize = new Size(windowSize, windowSize);
        MinimumSize = new Size(180, 180);
        MaximumSize = new Size(680, 680);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        BuildMenu();
        timer.Interval = 16;
        timer.Tick += OnTick;
        timer.Start();
        ResetTimer();
        FormClosing += delegate {
            SaveState();
            if (tomato != null) tomato.Dispose();
            if (tomatoCutout != null) tomatoCutout.Dispose();
            if (cachedBase != null) cachedBase.Dispose();
            if (cachedTomato != null) cachedTomato.Dispose();
            if (frameBitmap != null) frameBitmap.Dispose();
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x00080000;
            return cp;
        }
    }

    private void LoadTomato()
    {
        Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TomatoImage");
        if (stream == null) throw new InvalidOperationException("Tomato image resource is missing.");
        using (stream) using (Image source = Image.FromStream(stream)) tomato = new Bitmap(source);
        tomatoCutout = MakeCutout((Bitmap)tomato);
    }

    private static bool IsBackground(Color color)
    {
        int max = Math.Max(color.R, Math.Max(color.G, color.B));
        int min = Math.Min(color.R, Math.Min(color.G, color.B));
        return min > 205 && max - min < 42;
    }

    private static Bitmap MakeCutout(Bitmap source)
    {
        int width = source.Width;
        int height = source.Height;
        bool hasAlpha = source.GetPixel(0, 0).A < 250;
        if (hasAlpha)
        {
            int alphaLeft = width, alphaTop = height, alphaRight = 0, alphaBottom = 0;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                if (source.GetPixel(x, y).A < 2) continue;
                alphaLeft = Math.Min(alphaLeft, x); alphaTop = Math.Min(alphaTop, y); alphaRight = Math.Max(alphaRight, x); alphaBottom = Math.Max(alphaBottom, y);
            }
            Rectangle crop = Rectangle.FromLTRB(alphaLeft, alphaTop, alphaRight + 1, alphaBottom + 1);
            return source.Clone(crop, PixelFormat.Format32bppArgb);
        }
        bool[] background = new bool[width * height];
        Queue<Point> queue = new Queue<Point>();
        Action<int, int> seed = delegate(int x, int y) {
            int index = y * width + x;
            if (!background[index] && IsBackground(source.GetPixel(x, y)))
            {
                background[index] = true;
                queue.Enqueue(new Point(x, y));
            }
        };
        for (int x = 0; x < width; x++) { seed(x, 0); seed(x, height - 1); }
        for (int y = 0; y < height; y++) { seed(0, y); seed(width - 1, y); }
        int[] dx = new int[] { -1, 1, 0, 0 };
        int[] dy = new int[] { 0, 0, -1, 1 };
        while (queue.Count > 0)
        {
            Point point = queue.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int x = point.X + dx[i], y = point.Y + dy[i];
                if (x < 0 || y < 0 || x >= width || y >= height) continue;
                int index = y * width + x;
                if (!background[index] && IsBackground(source.GetPixel(x, y)))
                {
                    background[index] = true;
                    queue.Enqueue(new Point(x, y));
                }
            }
        }
        int left = width, top = height, right = 0, bottom = 0;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            if (background[y * width + x]) continue;
            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        left = Math.Max(0, left - 1); top = Math.Max(0, top - 1);
        right = Math.Min(width - 1, right + 1); bottom = Math.Min(height - 1, bottom + 1);
        Bitmap result = new Bitmap(right - left + 1, bottom - top + 1, PixelFormat.Format32bppArgb);
        for (int y = top; y <= bottom; y++) for (int x = left; x <= right; x++)
        {
            if (background[y * width + x]) continue;
            int nearBackground = 0;
            for (int oy = -1; oy <= 1; oy++) for (int ox = -1; ox <= 1; ox++)
            {
                int nx = x + ox, ny = y + oy;
                if (nx >= 0 && ny >= 0 && nx < width && ny < height && background[ny * width + nx]) nearBackground++;
            }
            Color color = source.GetPixel(x, y);
            int alpha = nearBackground == 0 ? 255 : Math.Max(96, 255 - nearBackground * 20);
            result.SetPixel(x - left, y - top, Color.FromArgb(alpha, color.R, color.G, color.B));
        }
        return result;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RebuildVisualCache();
        RenderLayered();
    }

    private void RebuildVisualCache()
    {
        if (tomato == null || ClientSize.Width < 1 || ClientSize.Height < 1) return;
        if (cachedBase != null) cachedBase.Dispose();
        if (cachedTomato != null) cachedTomato.Dispose();
        if (frameBitmap != null) frameBitmap.Dispose();
        cachedBase = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
        cachedTomato = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
        frameBitmap = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
        RectangleF dial = new RectangleF(2, 2, ClientSize.Width - 4, ClientSize.Height - 4);
        using (Graphics g = Graphics.FromImage(cachedTomato))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            float scale = Math.Min(dial.Width / tomatoCutout.Width, dial.Height / tomatoCutout.Height);
            float width = tomatoCutout.Width * scale, height = tomatoCutout.Height * scale;
            RectangleF destination = new RectangleF((ClientSize.Width - width) / 2f, (ClientSize.Height - height) / 2f, width, height);
            g.DrawImage(tomatoCutout, destination);
        }
        for (int y = 0; y < cachedTomato.Height; y++) for (int x = 0; x < cachedTomato.Width; x++)
        {
            Color pixel = cachedTomato.GetPixel(x, y);
            if (pixel.A > 0)
            {
                int noise = ((x * 17 + y * 31) % 9) - 4;
                int shade = Math.Max(200, Math.Min(220, 210 + noise));
                int alpha = pixel.A * 112 / 255;
                cachedBase.SetPixel(x, y, Color.FromArgb(alpha, shade, shade + 1, shade + 2));
            }
        }
        basePixels = ReadPixels(cachedBase);
        tomatoPixels = ReadPixels(cachedTomato);
        breakPixels = new int[tomatoPixels.Length];
        for (int i = 0; i < tomatoPixels.Length; i++) breakPixels[i] = MakeBreakPixel(tomatoPixels[i]);
        framePixels = new int[basePixels.Length];
        angleMap = new float[basePixels.Length];
        featherMap = new float[basePixels.Length];
        float centerX = ClientSize.Width / 2f, centerY = ClientSize.Height / 2f;
        for (int y = 0; y < ClientSize.Height; y++) for (int x = 0; x < ClientSize.Width; x++)
        {
            int index = y * ClientSize.Width + x;
            float dx = x + .5f - centerX, dy = y + .5f - centerY;
            double angle = Math.Atan2(dx, -dy);
            if (angle < 0) angle += Math.PI * 2;
            float radius = (float)Math.Sqrt(dx * dx + dy * dy);
            angleMap[index] = (float)angle;
            featherMap[index] = Math.Min(.15f, 1.35f / Math.Max(9f, radius));
        }
    }

    private static int[] ReadPixels(Bitmap bitmap)
    {
        Rectangle rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        int[] pixels = new int[bitmap.Width * bitmap.Height];
        Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        bitmap.UnlockBits(data);
        return pixels;
    }

    private static void WritePixels(Bitmap bitmap, int[] pixels)
    {
        Rectangle rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bitmap.UnlockBits(data);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RenderLayered();
    }

    private void RenderLayered()
    {
        if (!IsHandleCreated || frameBitmap == null || basePixels == null || tomatoPixels == null) return;
        int[] activePixels = focusPhase ? tomatoPixels : breakPixels;
        float fraction = total <= 0 ? 0f : (float)Math.Max(0, Math.Min(1, remaining / total));
        if (fraction >= .999999f) Array.Copy(activePixels, framePixels, framePixels.Length);
        else if (fraction <= .000001f) Array.Copy(basePixels, framePixels, framePixels.Length);
        else
        {
            float sweep = fraction * (float)(Math.PI * 2);
            for (int i = 0; i < framePixels.Length; i++)
            {
                float edge = featherMap[i];
                float coverage = .5f + (sweep - angleMap[i]) / (edge * 2f);
                if (coverage <= 0) framePixels[i] = basePixels[i];
                else if (coverage >= 1) framePixels[i] = activePixels[i];
                else framePixels[i] = BlendPixel(basePixels[i], activePixels[i], coverage);
            }
        }
        WritePixels(frameBitmap, framePixels);

        using (Graphics g = Graphics.FromImage(frameBitmap))
        {
            float dot = Math.Max(2.5f, ClientSize.Width / 130f);
            float gap = dot * 2.15f;
            float cx = ClientSize.Width * 0.76f;
            float cy = ClientSize.Height * 0.16f;
            int hitWidth = Math.Max(34, (int)(ClientSize.Width * .16f));
            int hitHeight = Math.Max(28, (int)(ClientSize.Height * .12f));
            menuHit = new Rectangle((int)cx - hitWidth / 2, (int)cy - hitHeight / 2, hitWidth, hitHeight);
            if (showMenuDots || menu.Visible)
            {
                using (Brush shadow = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                using (Brush light = new SolidBrush(Color.FromArgb(225, 255, 255, 255)))
                {
                    for (int i = -1; i <= 1; i++)
                    {
                        float x = cx + i * gap;
                        g.FillEllipse(shadow, x - dot + 1, cy - dot + 1, dot * 2, dot * 2);
                        g.FillEllipse(light, x - dot, cy - dot, dot * 2, dot * 2);
                    }
                }
            }
        }
        Present(frameBitmap);
    }

    private static int BlendPixel(int from, int to, float amount)
    {
        int b = (int)((from & 255) + ((to & 255) - (from & 255)) * amount);
        int g = (int)(((from >> 8) & 255) + (((to >> 8) & 255) - ((from >> 8) & 255)) * amount);
        int r = (int)(((from >> 16) & 255) + (((to >> 16) & 255) - ((from >> 16) & 255)) * amount);
        int a = (int)(((from >> 24) & 255) + (((to >> 24) & 255) - ((from >> 24) & 255)) * amount);
        return b | (g << 8) | (r << 16) | (a << 24);
    }

    private static int MakeBreakPixel(int pixel)
    {
        int a = (pixel >> 24) & 255;
        if (a == 0) return 0;
        float scale = 255f / a;
        float b0 = (pixel & 255) * scale;
        float g0 = ((pixel >> 8) & 255) * scale;
        float r0 = ((pixel >> 16) & 255) * scale;
        float light = Math.Min(255f, .299f * r0 + .587f * g0 + .114f * b0);
        int gray = Math.Min(255, (int)light) * a / 255;
        return gray | (gray << 8) | (gray << 16) | (a << 24);
    }

    private void Present(Bitmap bitmap)
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        IntPtr hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(memory, hBitmap);
        NativePoint destination = new NativePoint(Left, Top);
        NativeSize size = new NativeSize(bitmap.Width, bitmap.Height);
        NativePoint source = new NativePoint(0, 0);
        BlendFunction blend = new BlendFunction { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
        UpdateLayeredWindow(Handle, screen, ref destination, ref size, memory, ref source, 0, ref blend, 2);
        SelectObject(memory, old);
        DeleteObject(hBitmap);
        DeleteDC(memory);
        ReleaseDC(IntPtr.Zero, screen);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right || (e.Button == MouseButtons.Left && menuHit.Contains(e.Location)))
        {
            RefreshMenu();
            menu.Show(this, e.Location);
            return;
        }
        if (e.Button == MouseButtons.Left)
        {
            mouseDown = e.Location;
            mouseMoved = false;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool hoverDots = menuHit.Contains(e.Location);
        if (hoverDots != showMenuDots)
        {
            showMenuDots = hoverDots;
            RenderLayered();
        }
        if (e.Button != MouseButtons.Left || menu.Visible || menuHit.Contains(mouseDown)) return;
        if (Math.Abs(e.X - mouseDown.X) + Math.Abs(e.Y - mouseDown.Y) < 6) return;
        mouseMoved = true;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, 2, 0);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!menu.Visible && showMenuDots)
        {
            showMenuDots = false;
            RenderLayered();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && !mouseMoved && !menuHit.Contains(e.Location)) Toggle();
    }

    private void BuildMenu()
    {
        menu.RenderMode = ToolStripRenderMode.System;
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = true;
        startItem = AddItem(menu.Items, "开始", delegate { Toggle(); });
        AddItem(menu.Items, "重置", delegate { ResetTimer(); });
        AddItem(menu.Items, "跳过当前阶段", delegate { Skip(); });
        menu.Items.Add(new ToolStripSeparator());

        rhythmMenu = new ToolStripMenuItem("专注节奏");
        rhythm50Item = AddItem(rhythmMenu.DropDownItems, "50 分钟 / 10 分钟", delegate { SetRhythm(true); });
        rhythm25Item = AddItem(rhythmMenu.DropDownItems, "25 分钟 / 5 分钟", delegate { SetRhythm(false); });
        menu.Items.Add(rhythmMenu);

        countMenu = new ToolStripMenuItem("执行次数");
        int[] counts = new int[] { 1, 2, 4, 8 };
        foreach (int value in counts)
        {
            int captured = value;
            countItems[value] = AddItem(countMenu.DropDownItems, value.ToString() + " 次", delegate { SetRepetitions(captured); });
        }
        infiniteItem = AddItem(countMenu.DropDownItems, "无限循环", delegate { SetInfinite(); });
        menu.Items.Add(countMenu);

        sizeMenu = new ToolStripMenuItem("大小调节");
        int[] sizes = new int[] { 180, 220, 260, 300, 340, 380, 440, 520, 600, 680 };
        foreach (int value in sizes)
        {
            int captured = value;
            sizeItems[value] = AddItem(sizeMenu.DropDownItems, value.ToString() + " px", delegate { SetSize(captured); });
        }
        menu.Items.Add(sizeMenu);

        statsItem = new ToolStripMenuItem("专注记录");
        menu.Items.Add(statsItem);
        statusItem = new ToolStripMenuItem("");
        statusItem.Enabled = false;
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        topMostItem = AddItem(menu.Items, "始终置顶", delegate {
            alwaysOnTop = !alwaysOnTop;
            TopMost = alwaysOnTop;
            RefreshMenu();
            SaveState();
        });
        topMostItem.CheckOnClick = false;
        AddItem(menu.Items, "最小化", delegate { WindowState = FormWindowState.Minimized; });
        AddItem(menu.Items, "退出", delegate { Close(); });
        menu.Opening += delegate { RefreshMenu(); };
    }

    private static ToolStripMenuItem AddItem(ToolStripItemCollection items, string text, EventHandler action)
    {
        ToolStripMenuItem item = new ToolStripMenuItem(text);
        item.Click += action;
        items.Add(item);
        return item;
    }

    private void RefreshMenu()
    {
        startItem.Text = running ? "暂停" : "开始";
        topMostItem.Checked = alwaysOnTop;
        topMostItem.Text = "始终置顶 · " + (alwaysOnTop ? "已开启" : "未开启");

        rhythm50Item.Checked = preset50;
        rhythm25Item.Checked = !preset50;
        rhythmMenu.Text = "专注节奏 · " + (preset50 ? "50 / 10" : "25 / 5");

        foreach (KeyValuePair<int, ToolStripMenuItem> pair in countItems)
            pair.Value.Checked = !infinite && pair.Key == repetitions;
        infiniteItem.Checked = infinite;
        countMenu.Text = "执行次数 · " + (infinite ? "无限循环" : repetitions + " 次");

        foreach (KeyValuePair<int, ToolStripMenuItem> pair in sizeItems)
            pair.Value.Checked = pair.Key == windowSize;
        sizeMenu.Text = "大小调节 · " + windowSize + " px";

        string phaseText = focusPhase ? "专注" : "休息";
        string rhythmText = preset50 ? "50 / 10" : "25 / 5";
        statusItem.Text = infinite
            ? string.Format("当前：{0} · {1} · 已完成 {2} 次 · 无限", phaseText, rhythmText, completed)
            : string.Format("当前：{0} · {1} · 已完成 {2} / {3} 次", phaseText, rhythmText, completed, repetitions);
        statsItem.DropDownItems.Clear();
        AddStat("今天", delegate(DateTime d) { return d.Date == DateTime.Today; });
        DateTime monday = DateTime.Today.AddDays(-((int)DateTime.Today.DayOfWeek + 6) % 7);
        AddStat("本周", delegate(DateTime d) { return d.Date >= monday; });
        AddStat("本月", delegate(DateTime d) { return d.Year == DateTime.Today.Year && d.Month == DateTime.Today.Month; });
        AddStat("今年", delegate(DateTime d) { return d.Year == DateTime.Today.Year; });
    }

    private void AddStat(string label, Predicate<DateTime> include)
    {
        double seconds = 0;
        foreach (KeyValuePair<string, double> pair in stats)
        {
            DateTime date;
            if (DateTime.TryParse(pair.Key, out date) && include(date)) seconds += pair.Value;
        }
        ToolStripMenuItem item = new ToolStripMenuItem(label + "  " + FormatDuration(seconds));
        item.Enabled = false;
        statsItem.DropDownItems.Add(item);
    }

    private static string FormatDuration(double seconds)
    {
        int minutes = (int)(seconds / 60);
        if (minutes < 60) return minutes + " 分钟";
        return string.Format("{0} 小时 {1} 分", minutes / 60, minutes % 60);
    }

    private void Toggle()
    {
        running = !running;
        lastTick = DateTime.UtcNow;
        RenderLayered();
    }

    private int FocusMinutes
    {
        get { return preset50 ? 50 : 25; }
    }

    private int BreakMinutes
    {
        get { return preset50 ? 10 : 5; }
    }

    private double PhaseDuration(bool isFocus)
    {
        return (isFocus ? FocusMinutes : BreakMinutes) * 60.0;
    }

    private void SetRhythm(bool useFiftyMinutes)
    {
        preset50 = useFiftyMinutes;
        ResetTimer();
        RefreshMenu();
        SaveState();
    }

    private void SetRepetitions(int value)
    {
        repetitions = value;
        infinite = false;
        RefreshMenu();
        SaveState();
    }

    private void SetInfinite()
    {
        infinite = true;
        RefreshMenu();
        SaveState();
    }

    private void ResetTimer()
    {
        running = false;
        focusPhase = true;
        completed = 0;
        total = PhaseDuration(true);
        remaining = total;
        RenderLayered();
    }

    private void Skip()
    {
        focusPhase = !focusPhase;
        total = PhaseDuration(focusPhase);
        remaining = total;
        lastTick = DateTime.UtcNow;
        RenderLayered();
    }

    private void OnTick(object sender, EventArgs e)
    {
        if (!running) return;
        DateTime now = DateTime.UtcNow;
        double elapsed = Math.Min(3, Math.Max(0, (now - lastTick).TotalSeconds));
        lastTick = now;
        Advance(elapsed);
        RenderLayered();
    }

    private void Advance(double seconds)
    {
        while (seconds > 0 && running)
        {
            double used = Math.Min(seconds, remaining);
            if (focusPhase) AddFocus(used);
            remaining -= used;
            seconds -= used;
            if (remaining <= 0.0001) Transition();
        }
    }

    private void Transition()
    {
        System.Media.SystemSounds.Asterisk.Play();
        if (focusPhase)
        {
            completed++;
            if (!infinite && completed >= repetitions)
            {
                remaining = 0;
                running = false;
                SaveState();
                return;
            }
            focusPhase = false;
            total = PhaseDuration(false);
        }
        else
        {
            focusPhase = true;
            total = PhaseDuration(true);
        }
        remaining = total;
    }

    private void AddFocus(double seconds)
    {
        string key = DateTime.Today.ToString("yyyy-MM-dd");
        if (!stats.ContainsKey(key)) stats[key] = 0;
        stats[key] += seconds;
        unsaved += seconds;
        if (unsaved >= 30) SaveState();
    }

    private void SetSize(int size)
    {
        size = Math.Max(180, Math.Min(680, size));
        Point center = new Point(Left + Width / 2, Top + Height / 2);
        windowSize = size;
        ClientSize = new Size(size, size);
        Location = new Point(center.X - Width / 2, center.Y - Height / 2);
        RefreshMenu();
        SaveState();
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(settingsPath))
            {
                foreach (string line in File.ReadAllLines(settingsPath))
                {
                    string[] part = line.Split(new char[] { '=' }, 2);
                    if (part.Length != 2) continue;
                    if (part[0] == "version") int.TryParse(part[1], out stateVersion);
                    else if (part[0] == "preset50") bool.TryParse(part[1], out preset50);
                    else if (part[0] == "repetitions") int.TryParse(part[1], out repetitions);
                    else if (part[0] == "infinite") bool.TryParse(part[1], out infinite);
                    else if (part[0] == "alwaysOnTop") bool.TryParse(part[1], out alwaysOnTop);
                    else if (part[0] == "size") int.TryParse(part[1], out windowSize);
                }
            }
            if (stateVersion < 2) windowSize = 360;
            windowSize = Math.Max(180, Math.Min(680, windowSize));
            repetitions = Math.Max(1, Math.Min(99, repetitions));
            if (File.Exists(statsPath))
            {
                foreach (string line in File.ReadAllLines(statsPath))
                {
                    string[] part = line.Split('\t');
                    double value;
                    if (part.Length == 2 && double.TryParse(part[1], out value)) stats[part[0]] = value;
                }
            }
        }
        catch { }
    }

    private void SaveState()
    {
        try
        {
            File.WriteAllLines(settingsPath, new string[] {
                "version=2", "preset50=" + preset50, "repetitions=" + repetitions,
                "infinite=" + infinite, "alwaysOnTop=" + alwaysOnTop, "size=" + windowSize
            });
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, double> pair in stats) lines.Add(pair.Key + "\t" + pair.Value);
            File.WriteAllLines(statsPath, lines.ToArray());
            unsaved = 0;
        }
        catch { }
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TomatoTimerForm());
    }
}
