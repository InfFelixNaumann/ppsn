﻿-------------------------------------------------------------------------------
-- Define Commands
-------------------------------------------------------------------------------

-- todo: Speichern, Rückgangig, Wiederholen -> durch maske selbst
SaveCommand = command(
	function (arg) : void
		msgbox("Speichern...");
	end
);

TestStackCommand = command(
	function (arg) : void

		CommitEdit();
		
		local sb = clr.System.Text.StringBuilder();
		foreach c in UndoManager do
			sb:AppendLine(c.Type .. " - " .. c.Description);
		end;
		msgbox(sb:ToString());

	end
);

UndoCommand = command(
	function (arg) : void
		CommitEdit();
		UndoManager:Undo();
	end,
	function (arg) : bool
		return UndoManager.CanUndo;
	end
);

RedoCommand = command(
	function (arg) : void
		CommitEdit();
		UndoManager:Redo();
	end,
	function (arg) : bool
		return UndoManager.CanRedo;
	end
);


NewAddressCommand = command(
	function (arg) : void

		CommitEdit();

		do (trans = UndoManager:BeginTransaction("Neue Adresse"))
			Data.KONT.First.ADRE:Add({ ADRENAME = "Neue Adresse"});
			--error("test");
			trans:Commit();
		end;

	end
);

NewPartnerCommand = command(
	function (arg) : void
		CommitEdit();

		local cur = APART_TreeView.SelectedValue;

		if cur.Table.Name == "ADRE" then
			do (trans = UndoManager:BeginTransaction("Neuer Partner"))
				cur.ANSP:Add({ANSPNAME = "Neuer Partner"});
				trans:Commit();
			end;
		end;
	end
);

RemovePosCommand = command(
	function (arg) : void
		CommitEdit();

		local cur = APART_TreeView.SelectedValue;
		if cur ~= nil then
			do (trans = UndoManager:BeginTransaction("Lösche " .. (cur.ADRENAME or cur.ANSPNAME)))
				cur:Remove();
				trans:Commit();
			end;
		end;
	end
);

--[[

Add = command(function () : void
	print("Hallo Welt");
	do (operation = UndoManager:BeginTransaction("Kontaktname ändern"))
		Data.KONT.First.KONTNAME = "KONTNAME - Changed Value ";
		Data.KONT.First.KONTvorNAME = "KONTvorNAME - Changed Value ";
		operation:Commit();
	end;
end);

]]