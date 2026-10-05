import { createApp } from 'vue';
import '../styles/settings.css';
import SettingsApp from './SettingsApp.vue';

// 设置窗刻意不接光学校正：量具实测它的行内文字本来就在格网上（law off 散差 0.50 设备px
// = 共线极限，law on 反而被推到 1.50）。见 scripts/alignment/ 与 ADR-0006。
createApp(SettingsApp).mount('#app');
