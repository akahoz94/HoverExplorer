using System;
using System.Collections.Generic;

namespace HoverExplorer
{
    internal static class I18n
    {
        public static string Lang = "zh";

        public static readonly string[] LangCodes = { "zh", "en", "ja", "ko", "fr", "ru", "es" };
        public static readonly string[] LangNames = { "中文", "English", "日本語", "한국어", "Français", "Русский", "Español" };

        // 键 → { zh, en, ja, ko, fr, ru, es }
        private static readonly Dictionary<string, string[]> _res = new Dictionary<string, string[]>
        {
            {"pin", new[]{"钉","Pin","ピン","핀","Épingler","Закр.","Fijar"}},
            {"preview", new[]{"预览","Preview","プレビュー","미리보기","Aperçu","Просмотр","Vista"}},
            {"search", new[]{"搜","Search","検索","검색","Chercher","Поиск","Buscar"}},
            {"items", new[]{"项","items","項目","항목","élém.","элем.","elem"}},
            {"button_tips", new[]{"按钮提示","Button tooltips","ボタンのヒント","버튼 도움말","Infobulles boutons","Подсказки кнопок","Información de botones"}},
            {"tip_pin_panel", new[]{"固定此窗口（再点取消）","Pin this window (click again to unpin)","このウィンドウを固定（再クリックで解除）","이 창 고정(다시 클릭 시 해제)","Épingler cette fenêtre (recliquer pour désépingler)","Закрепить окно (повторно — открепить)","Fijar esta ventana (clic de nuevo para soltar)"}},
            {"tip_pin_card", new[]{"固定为桌面卡片","Pin as desktop card","デスクトップカードとして固定","데스크톱 카드로 고정","Épingler comme carte","Закрепить как карточку","Fijar como tarjeta"}},
            {"tip_copy_menu", new[]{"复制（文本/文件/路径）","Copy (text/file/path)","コピー（テキスト/ファイル/パス）","복사(텍스트/파일/경로)","Copier (texte/fichier/chemin)","Копировать (текст/файл/путь)","Copiar (texto/archivo/ruta)"}},
            {"tip_save", new[]{"保存修改","Save changes","変更を保存","변경 저장","Enregistrer","Сохранить","Guardar cambios"}},
            {"tip_sort", new[]{"排序方式（点击切换正序/倒序）","Sort order (click to toggle asc/desc)","並び替え（クリックで昇順/降順）","정렬(클릭 시 오름/내림차순)","Ordre de tri (cliquer pour asc/desc)","Сортировка (нажмите для порядка)","Orden (clic para asc/desc)"}},
            {"tip_rename_dir", new[]{"点击重命名此文件夹","Click to rename this folder","クリックでこのフォルダ名を変更","클릭하여 이 폴더 이름 변경","Cliquer pour renommer ce dossier","Нажмите, чтобы переименовать папку","Clic para renombrar esta carpeta"}},
            {"tip_search", new[]{"搜索（Ctrl+F 展开）","Search (Ctrl+F to show)","検索（Ctrl+F で表示）","검색(Ctrl+F 표시)","Rechercher (Ctrl+F pour afficher)","Поиск (Ctrl+F)","Buscar (Ctrl+F)"}},
            {"tip_resize", new[]{"拖动调整大小","Drag to resize","ドラッグでサイズ変更","드래그하여 크기 조절","Glisser pour redimensionner","Перетащите для изменения размера","Arrastrar para redimensionar"}},
            {"delete_n", new[]{"删除({0})","Delete({0})","削除({0})","삭제({0})","Suppr({0})","Удалить({0})","Eliminar({0})"}},
            {"copy_path_n", new[]{"复制路径({0})","Copy path({0})","パスコピー({0})","경로복사({0})","Copier chemin({0})","Коп. путь({0})","Copiar ruta({0})"}},
            {"cancel_sel", new[]{"取消选择","Clear selection","選択解除","선택해제","Effacer sélec.","Снять выбор","Borrar selección"}},
            {"open", new[]{"打开","Open","開く","열기","Ouvrir","Открыть","Abrir"}},
            {"new_window", new[]{"新窗口","New window","新規ウィンドウ","새 창","Nouvelle fenêtre","Новое окно","Nueva ventana"}},
            {"rename", new[]{"重命名","Rename","名前変更","이름변경","Renommer","Переименовать","Cambiar nombre"}},
            {"delete", new[]{"删除","Delete","削除","삭제","Supprimer","Удалить","Eliminar"}},
            {"copy_path", new[]{"复制路径","Copy path","パスコピー","경로복사","Copier chemin","Коп. путь","Copiar ruta"}},
            {"copy_name", new[]{"复制名称","Copy name","名前コピー","이름복사","Copier nom","Коп. имя","Copiar nombre"}},
            {"edit", new[]{"编辑","Edit","編集","편집","Éditer","Изменить","Editar"}},
            {"native_menu", new[]{"原生菜单","Native menu","ネイティブメニュー","네이티브 메뉴","Menu natif","Родное меню","Menú nativo"}},
            {"copy_file", new[]{"复制文件","Copy file","ファイルコピー","파일 복사","Copier fichier","Коп. файл","Copiar archivo"}},
            {"copied", new[]{"已复制","Copied","コピー済み","복사됨","Copié","Скопировано","Copiado"}},
            {"copy_selected", new[]{"复制选中文本","Copy selected text","選択テキストコピー","선택 텍스트 복사","Copier sélec.","Коп. выдел.","Copiar selec."}},
            {"copy_all", new[]{"复制全文","Copy all text","全テキストコピー","전체 텍스트 복사","Copier tout","Коп. всё","Copiar todo"}},
            {"sort_asc", new[]{"▲ 正序","▲ Ascending","▲ 昇順","▲ 오름차순","▲ Croissant","▲ По возр.","▲ Ascendente"}},
            {"sort_desc", new[]{"▼ 倒序","▼ Descending","▼ 降順","▼ 내림차순","▼ Décroissant","▼ По убыв.","▼ Descendente"}},
            {"delete_confirm", new[]{"删除 {0} 项到回收站？","Delete {0} item(s) to recycle bin?","{0}項目をゴミ箱に削除しますか？","{0}개 항목을 휴지통으로 삭제?","Supprimer {0} élément(s) vers la corbeille ?","Удалить {0} элем. в корзину?","¿Eliminar {0} elemento(s) a la papelera?"}},
            {"delete_title", new[]{"删除","Delete","削除","삭제","Supprimer","Удалить","Eliminar"}},
            {"explorer_hover", new[]{"资源管理器悬停","Explorer hover","エクスプローラー ホバー","탐색기 호버","Survol explorateur","Наведение в проводнике","Hover en explorador"}},
            {"file_preview", new[]{"文件预览","File preview","ファイルプレビュー","파일 미리보기","Aperçu fichier","Предпросмотр файла","Vista previa archivo"}},
            {"exit", new[]{"退出","Exit","終了","종료","Quitter","Выход","Salir"}},
            {"language", new[]{"语言","Language","言語","언어","Langue","Язык","Idioma"}},
            {"sort_name", new[]{"名称","Name","名前","이름","Nom","Имя","Nombre"}},
            {"sort_size", new[]{"大小","Size","サイズ","크기","Taille","Размер","Tamaño"}},
            {"sort_date", new[]{"日期","Date","日付","날짜","Date","Дата","Fecha"}},
            {"font_label", new[]{"字体: ","Font: ","フォント: ","글꼴: ","Police : ","Шрифт: ","Fuente: "}},
            {"slide", new[]{"[幻灯片 {0}]","[Slide {0}]","[スライド {0}]","[슬라이드 {0}]","[Diapo {0}]","[Слайд {0}]","[Diapositiva {0}]"}},
            {"office_no_text", new[]{"(无法提取文本，可能是受保护或非标准格式)","(No text extracted, may be protected or non-standard)","(テキストを抽出できません。保護または非標準形式)","(텍스트 추출 불가. 보호되었거나 비표준 형식)","(Texte non extrait, peut être protégé ou non standard)","(Текст не извлечён, возможно защищён или нестандартный)","(No se extrajo texto, puede estar protegido o no estándar)"}},
            {"total_items", new[]{"… 共 {0} 项","... {0} items total","… 全{0}項目","… 총 {0}항목","… {0} éléments au total","… всего {0} элем.","... {0} elementos en total"}},
            {"no_bandizip", new[]{"未找到 Bandizip (bz.exe)，无法预览 .7z/.rar。","Bandizip (bz.exe) not found, cannot preview .7z/.rar.","Bandizip (bz.exe) が見つかりません。.7z/.rar をプレビューできません。","Bandizip (bz.exe)를 찾을 수 없습니다. .7z/.rar 미리보기 불가.","Bandizip (bz.exe) introuvable, prévisualisation .7z/.rar impossible.","Bandizip (bz.exe) не найден, предпросмотр .7z/.rar невозможен.","No se encontró Bandizip (bz.exe), no se puede previsualizar .7z/.rar."}},
            {"bz_failed", new[]{"调用 bz.exe 失败: ","bz.exe call failed: ","bz.exe 呼び出し失敗: ","bz.exe 호출 실패: ","Échec appel bz.exe : ","Ошибка вызова bz.exe: ","Error llamada bz.exe: "}},
            {"empty_archive", new[]{"(空压缩包或解析失败)","(Empty archive or parse failed)","(空のアーカイブまたは解析失敗)","(빈 아카이브 또는 구문 분석 실패)","(Archive vide ou échec d'analyse)","(Пустой архив или ошибка разбора)","(Archivo vacío o error de análisis)"}},
            {"file_label", new[]{"文件: ","File: ","ファイル: ","파일: ","Fichier : ","Файл: ","Archivo: "}},
            {"desc_label", new[]{"描述: ","Description: ","説明: ","설명: ","Description : ","Описание: ","Descripción: "}},
            {"product_label", new[]{"产品: ","Product: ","製品: ","제품: ","Produit : ","Продукт: ","Producto: "}},
            {"company_label", new[]{"公司: ","Company: ","会社: ","회사: ","Société : ","Компания: ","Empresa: "}},
            {"version_label", new[]{"版本: ","Version: ","バージョン: ","버전: ","Version : ","Версия: ","Versión: "}},
            {"origfile_label", new[]{"原文件: ","Original: ","元ファイル: ","원본: ","Original : ","Оригинал: ","Original: "}},
            {"type_label", new[]{"类型: ","Type: ","タイプ: ","유형: ","Type : ","Тип: ","Tipo: "}},
            {"size_label", new[]{"大小: ","Size: ","サイズ: ","크기: ","Taille : ","Размер: ","Tamaño: "}},
            {"managed_asm", new[]{".NET 托管程序集",".NET managed assembly",".NET マネージドアセンブリ",".NET 관리 어셈블리","Assembly .NET managé",".NET управляемая сборка","Ensamblado .NET gestionado"}},
            {"native_dll", new[]{"原生 DLL","Native DLL","ネイティブ DLL","네이티브 DLL","DLL native","Нативная DLL","DLL nativa"}},
            {"managed_exe", new[]{".NET 托管可执行",".NET managed executable",".NET マネージド実行可能",".NET 관리 실행 파일","Exécutable .NET managé",".NET управляемый exe","Ejecutable .NET gestionado"}},
            {"target_label", new[]{"目标: ","Target: ","ターゲット: ","대상: ","Cible : ","Цель: ","Destino: "}},
            {"args_label", new[]{"参数: ","Args: ","引数: ","인수: ","Args : ","Аргументы: ","Args: "}},
            {"workdir_label", new[]{"工作目录: ","Work dir: ","作業ディレクトリ: ","작업 디렉토리: ","Rép. travail : ","Раб. каталог: ","Dir. trabajo: "}},
            {"icon_label", new[]{"图标: ","Icon: ","アイコン: ","아이콰: ","Icône : ","Значок: ","Icono: "}},
            {"exists_label", new[]{"存在: ","Exists: ","存在: ","존재: ","Existe : ","Существует: ","Existe: "}},
            {"yes", new[]{"是","Yes","はい","예","Oui","Да","Sí"}},
            {"no", new[]{"否","No","いいえ","아니오","Non","Нет","No"}},
            {"parse_failed", new[]{"(解析失败: ","(Parse failed: ","(解析失敗: ","(구문 분석 실패: ","(Échec d'analyse : ","(Ошибка разбора: ","(Error de análisis: "}},
            {"title_label", new[]{"书名: ","Title: ","書名: ","제목: ","Titre : ","Название: ","Título: "}},
            {"author_label", new[]{"作者: ","Author: ","著者: ","저자: ","Auteur : ","Автор: ","Autor: "}},
            {"lang_label", new[]{"语言: ","Language: ","言語: ","언어: ","Langue : ","Язык: ","Idioma: "}},
            {"toc_header", new[]{"—— 目录 ——","—— Contents ——","—— 目次 ——","—— 목차 ——","—— Sommaire ——","—— Содержание ——","—— Índice ——"}},
            {"theme", new[]{"主题","Theme","テーマ","테마","Thème","Тема","Tema"}},
            {"theme_dark", new[]{"深色","Dark","ダーク","다크","Sombre","Тёмная","Oscuro"}},
            {"theme_light", new[]{"浅色","Light","ライト","라이트","Clair","Светлая","Claro"}},
            {"donate", new[]{"vx:jtugqivi","vx:jtugqivi","vx:jtugqivi","vx:jtugqivi","vx:jtugqivi","vx:jtugqivi","vx:jtugqivi"}},
            {"donated", new[]{"已复制微信号 jtugqivi","Copied WeChat: jtugqivi","WeChatコピー: jtugqivi","WeChat 복사: jtugqivi","WeChat copié: jtugqivi","WeChat скопирован: jtugqivi","WeChat copiado: jtugqivi"}},
            {"debug_log", new[]{"调试日志","Debug log","デバッグログ","디버그 로그","Journal debug","Журнал отладки","Registro dep."}},
            {"hide_tooltip", new[]{"关闭系统提示","Hide system tooltip","システムヒントを閉じる","시스템 툴팁 숨기기","Masquer infobulle système","Скрыть подсказки","Ocultar tooltip sistema"}},
            {"autostart", new[]{"开机自启","Run at startup","スタートアップに登録","시작 시 실행","Lancement au démarrage","Автозапуск","Iniciar con Windows"}},
            {"hide_tray", new[]{"隐藏托盘图标","Hide tray icon","トレイアイコンを隠す","트레이 아이콘 숨기기","Masquer l'icône","Скрыть значок","Ocultar icono"}},
            {"settings", new[]{"设置","Settings","設定","설정","Paramètres","Настройки","Ajustes"}},
            {"settings_hotkey", new[]{"快捷键设置","Hotkey settings","ホットキー設定","단축키 설정","Raccourci clavier","Настройка гор. клавиши","Config. atajo"}},
            {"hotkey_enabled", new[]{"启用快捷键","Enable hotkey","ホットキー有効","단축키 활성화","Activer raccourci","Включить гор. клавишу","Activar atajo"}},
            {"hotkey_modifiers", new[]{"修饰键","Modifiers","修飾キー","수정키","Modificateurs","Модификаторы","Modificadores"}},
            {"hotkey_key", new[]{"按键","Key","キー","키","Touche","Клавиша","Tecla"}},
            {"hotkey_current", new[]{"当前","Current","現在","현재","Actuel","Текущий","Actual"}},
            {"hotkey_press_key", new[]{"按键...","Press key...","キーを押す...","키 누르기...","Touche...","Клавиша...","Tecla..."}},
            {"hotkey_esc_cancel", new[]{"Esc 取消","Esc to cancel","Esc でキャンセル","Esc 취소","Esc annuler","Esc отмена","Esc cancelar"}},
            {"hotkey_click_hint", new[]{"点击设置","Click to set","クリックで設定","클릭하여 설정","Cliquer pour définir","Щёлкните","Clic para fijar"}},
            {"hotkey_none", new[]{"(无)","(None)","(なし)","(없음)","(Aucun)","(Нет)","(Ninguno)"}},
            {"hotkey_disabled", new[]{"已禁用","Disabled","無効","비활성화","Désactivé","Отключено","Desactivado"}},
            {"settings_ok", new[]{"确定","OK","OK","확인","OK","OK","OK"}},
            {"settings_cancel", new[]{"取消","Cancel","キャンセル","취소","Annuler","Отмена","Cancelar"}},
            {"hotkey_reg_fail", new[]{"快捷键注册失败，可能被其他程序占用","Hotkey registration failed, may be occupied by another program","ホットキー登録失敗、別のプログラムが使用中","단축키 등록 실패, 다른 프로그램이 사용 중","Échec enregistrement raccourci, peut être occupé","Ошибка регистрации гор. клавиши, возможно занят","Error registro atajo, posible conflicto"}},
            {"hotkey_saved", new[]{"快捷键已保存","Hotkey saved","ホットキー保存済み","단축키 저장됨","Raccourci enregistré","Гор. клавиша сохранена","Atajo guardado"}},
            {"hover_enabled", new[]{"悬停已启用","Hover enabled","ホバー有効","호버 활성화","Survol activé","Наведение включено","Hover activado"}},
            {"hover_disabled", new[]{"悬停已禁用","Hover disabled","ホバー無効","호버 비활성화","Survol désactivé","Наведение отключено","Hover desactivado"}},
            {"hotkey_desc", new[]{"快捷键功能：启用/禁用悬停","Hotkey action: toggle hover","ホットキー機能：ホバー切替","단축키 기능: 호버 전환","Raccourci : basculer le survol","Гор. клавиша: вкл/выкл наведение","Atajo: alternar hover"}},
            {"hover_delay", new[]{"悬停触发延时(秒)","Hover delay (sec)","ホバー遅延(秒)","호버 지연(초)","Délai de survol (s)","Задержка наведения (с)","Retraso de hover (s)"}},
            {"hover_delay_desc", new[]{"鼠标悬停多久后弹出预览","Delay before preview appears","プレビューが表示されるまでの時間","미리보기가 표시되기까지의 시간","Délai avant l'aperçu","Время до появления предпросмотра","Tiempo antes de la vista previa"}},
            {"pin_launch", new[]{"钉为启动卡片","Pin as launch card","ピン（起動カード）","핀 (실행 카드)","Épingler (carte lancement)","Закр. (карта запуска)","Fijar (tarjeta inicio)"}},
            {"unpin", new[]{"取消固定","Unpin","解除","고정해제","Détacher","Открепить","Soltar"}},
            {"preview_orig", new[]{"原图大小","Original size","オリジナルサイズ","원본 크기","Taille d'origine","Исходный размер","Tamaño original"}},
            {"empty", new[]{"(空文件夹)","(empty folder)","(空のフォルダ)","(빈 폴더)","(dossier vide)","(пустая папка)","(carpeta vacía)"}},
        };

        public static string T(string key)
        {
            string[] arr;
            if (_res.TryGetValue(key, out arr))
            {
                int idx = Array.IndexOf(LangCodes, Lang);
                if (idx < 0 || idx >= arr.Length) idx = 0;
                return arr[idx];
            }
            return key;
        }

        public static string T(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }
    }
}