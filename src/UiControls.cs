using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PowerPal {
    internal static class UiText {
        public const TextFormatFlags SingleLine=TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding;
        public static Size Measure(Control control,string text,Font font) {
            using(var g=control.CreateGraphics()) return TextRenderer.MeasureText(g,text,font,new Size(10000,10000),SingleLine);
        }
        public static int LineHeight(Control control,Font font) { return Measure(control,"ÁÉÅ gjpqy 0123456789",font).Height; }
        public static int WrappedHeight(Control control,int width) {
            using(var g=control.CreateGraphics()) return TextRenderer.MeasureText(g,control.Text,control.Font,new Size(Math.Max(1,width),10000),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl|TextFormatFlags.NoPrefix).Height;
        }
    }
    internal sealed class ProcessGrid : DataGridView {
        public ProcessGrid() { DoubleBuffered=true; }
        internal void ClickHeader(int column) { OnColumnHeaderMouseClick(new DataGridViewCellMouseEventArgs(column,-1,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))); }
    }
    internal sealed class SortHeaderCell : DataGridViewColumnHeaderCell {
        public float Scale=1;
        int Px(float value) { return (int)Math.Round(value*Scale); }
        internal Rectangle ArrowBounds(Rectangle cell) { return new Rectangle(cell.Right-Px(19),cell.Top+(cell.Height-Px(18))/2,Px(12),Px(18)); }
        internal Rectangle LabelBounds(Rectangle cell) { return new Rectangle(cell.Left+Px(8),cell.Top+Px(4),Math.Max(1,cell.Width-Px(32)),Math.Max(1,cell.Height-Px(8))); }
        internal void Draw(Graphics graphics,Rectangle bounds,DataGridViewCellStyle style) {
            bool active=SortGlyphDirection!=SortOrder.None;
            using(var fill=new SolidBrush(active?Palette.Selection:Palette.Bg)) graphics.FillRectangle(fill,bounds);
            var text=LabelBounds(bounds); var arrow=ArrowBounds(bounds);
            TextRenderer.DrawText(graphics,Convert.ToString(Value),style.Font,text,Palette.Text,UiText.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            int middle=arrow.Left+arrow.Width/2,center=arrow.Top+arrow.Height/2,gap=Math.Max(1,Px(2)),half=Math.Max(3,Px(4)),tip=Math.Max(3,Px(4));
            using(var up=new SolidBrush(SortGlyphDirection==SortOrder.Ascending?Palette.Mint:Palette.Muted))
            using(var down=new SolidBrush(SortGlyphDirection==SortOrder.Descending?Palette.Mint:Palette.Muted)) {
                graphics.FillPolygon(up,new[]{new Point(middle,center-gap-tip),new Point(middle-half,center-gap),new Point(middle+half,center-gap)});
                graphics.FillPolygon(down,new[]{new Point(middle-half,center+gap),new Point(middle+half,center+gap),new Point(middle,center+gap+tip)});
            }
            using(var border=new Pen(active?Palette.Mint:Palette.Line,Math.Max(1,Px(active?2:1)))) graphics.DrawLine(border,bounds.Left,bounds.Bottom-1,bounds.Right,bounds.Bottom-1);
        }
        protected override void Paint(Graphics graphics,Rectangle clipBounds,Rectangle cellBounds,int rowIndex,DataGridViewElementStates state,object value,object formattedValue,string errorText,DataGridViewCellStyle style,DataGridViewAdvancedBorderStyle borderStyle,DataGridViewPaintParts parts) {
            var saved=graphics.Save(); graphics.SetClip(Rectangle.Intersect(cellBounds,clipBounds),CombineMode.Intersect);
            Draw(graphics,cellBounds,style); graphics.Restore(saved);
        }
    }
}
